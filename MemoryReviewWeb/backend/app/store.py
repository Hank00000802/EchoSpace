"""Read-only access to Phase 2 Session JSON (schemaVersion 1.x) and its PNG screenshots.

Safety rules enforced here:
  * a session is only readable if the researcher allowed it at launch (allowlist);
  * the session id is validated, and the resolved folder must be a DIRECT child of
    {dataRoot}/research (no symlink / traversal escape);
  * screenshots are never requested by path: the path comes from session.json, must match
    ``screenshots/<cardId>.png`` exactly, and must resolve inside that session's screenshots folder;
  * nothing is ever written, renamed or deleted.
"""

from __future__ import annotations

import hashlib
import json
import logging
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from .config import SESSION_ID_RE, Settings
from .models import (
    ClassificationGroupView,
    ConnectionCardView,
    CountsView,
    IssueView,
    MemoryCardView,
    ScreenshotView,
    SessionMetaView,
    SessionView,
)

log = logging.getLogger("memory_review")

CARD_ID_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")
SCREENSHOT_REL_RE = re.compile(r"screenshots/([A-Za-z0-9_-]{1,64})\.png")
COLOR_RE = re.compile(r"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?")
SHA256_RE = re.compile(r"[0-9a-fA-F]{64}")

SUPPORTED_SCHEMA_MAJOR = "1"
MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_PNG_BYTES = 20 * 1024 * 1024
MAX_TEXT_CHARS = 20000
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"

# Display order used only when temporalClassification must be derived from timeCategory.
CANONICAL_CATEGORY_ORDER = ["Unclassified", "Past", "Transition", "Present"]


class ReviewError(Exception):
    def __init__(self, status: int, code: str, message: str):
        super().__init__(message)
        self.status = status
        self.code = code
        self.message = message


# Same body for "not allowed" and "does not exist" so the API cannot be used to probe for sessions.
def _not_available(reason: str) -> ReviewError:
    log.warning("session not available: %s", reason)
    return ReviewError(404, "session_not_available", "Session not available.")


# ---------------------------------------------------------------- tolerant value helpers


def _text(value: Any, limit: int = MAX_TEXT_CHARS) -> str:
    if not isinstance(value, str):
        return ""
    cleaned = "".join(ch for ch in value if ch in "\n\t" or ord(ch) >= 32)
    return cleaned[:limit]


def _int(value: Any, default: int = 0) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        return default
    return value


def _pos_int_or_none(value: Any) -> Optional[int]:
    v = _int(value, 0)
    return v if v > 0 else None


class SessionStore:
    def __init__(self, settings: Settings):
        self._allowed = settings.allowed_sessions
        self._research_root = settings.research_root.resolve()

    # ------------------------------------------------------------ session folder / json

    def _session_dir(self, session_id: str) -> Path:
        if not isinstance(session_id, str) or not SESSION_ID_RE.fullmatch(session_id):
            raise ReviewError(400, "invalid_session_id", "Invalid session id.")
        if session_id not in self._allowed:
            raise _not_available("%s is not in the allowlist" % session_id)

        candidate = self._research_root / session_id
        try:
            resolved = candidate.resolve(strict=True)
        except (OSError, RuntimeError):
            raise _not_available("%s folder not found" % session_id)
        if resolved.parent != self._research_root or not resolved.is_dir():
            raise _not_available("%s resolves outside research root" % session_id)
        return resolved

    def _load_raw(self, session_id: str) -> Tuple[Path, Dict[str, Any]]:
        session_dir = self._session_dir(session_id)
        path = session_dir / "session.json"
        try:
            if path.is_symlink() or not path.is_file():
                raise _not_available("%s has no session.json" % session_id)
            if path.stat().st_size > MAX_JSON_BYTES:
                raise ReviewError(422, "session_unreadable", "session.json is too large.")
            raw = json.loads(path.read_bytes().decode("utf-8-sig"))
        except ReviewError:
            raise
        except (OSError, UnicodeDecodeError, ValueError) as ex:
            log.warning("cannot read session.json for %s: %s", session_id, ex)
            raise ReviewError(422, "session_unreadable", "session.json could not be read.")

        if not isinstance(raw, dict):
            raise ReviewError(422, "session_unreadable", "session.json has an unexpected structure.")

        schema = _text(raw.get("schemaVersion"), 20)
        if schema.split(".")[0] != SUPPORTED_SCHEMA_MAJOR:
            raise ReviewError(422, "unsupported_schema", "Unsupported schemaVersion.")
        if raw.get("sessionId") != session_id:
            # Folder name and content disagree: refuse rather than show possibly mixed data.
            log.warning("sessionId mismatch in folder %s", session_id)
            raise ReviewError(422, "session_mismatch", "session.json does not belong to this session.")
        return session_dir, raw

    # ------------------------------------------------------------ screenshots

    def _locate_screenshot(
        self, session_dir: Path, card_id: str, rel: Any, record: Optional[Dict[str, Any]], verify_hash: bool
    ) -> Tuple[Optional[bytes], Optional[str]]:
        """Returns (png_bytes_or_None, issue_code_or_None).

        With verify_hash=False the file is only stat'ed / magic-checked (cheap, for the session view);
        bytes are returned only when verify_hash=True.
        """
        if rel is None or rel == "":
            return None, None  # genuinely no screenshot (not an error)

        match = SCREENSHOT_REL_RE.fullmatch(rel) if isinstance(rel, str) else None
        if match is None or match.group(1) != card_id:
            return None, "screenshot_path_invalid"

        shots_dir = session_dir / "screenshots"
        target = shots_dir / (card_id + ".png")
        try:
            if target.is_symlink():
                return None, "screenshot_path_invalid"
            resolved = target.resolve(strict=True)
            if resolved.parent != shots_dir.resolve(strict=True) or not resolved.is_file():
                return None, "screenshot_path_invalid"
            size = resolved.stat().st_size
            if size <= 0 or size > MAX_PNG_BYTES:
                return None, "screenshot_file_invalid"
            expected_size = _pos_int_or_none(record.get("sizeBytes")) if record else None
            if expected_size is not None and expected_size != size:
                return None, "screenshot_integrity_mismatch"
            with resolved.open("rb") as fh:
                data = fh.read(MAX_PNG_BYTES + 1) if verify_hash else fh.read(len(PNG_MAGIC))
        except FileNotFoundError:
            return None, "screenshot_file_missing"
        except OSError:
            return None, "screenshot_file_invalid"

        if not data.startswith(PNG_MAGIC):
            return None, "screenshot_file_invalid"

        if verify_hash:
            expected_hash = record.get("sha256") if record else None
            if isinstance(expected_hash, str) and SHA256_RE.fullmatch(expected_hash):
                if hashlib.sha256(data).hexdigest() != expected_hash.lower():
                    return None, "screenshot_integrity_mismatch"
            return data, None
        return b"", None

    def read_screenshot(self, session_id: str, card_id: str) -> bytes:
        if not isinstance(card_id, str) or not CARD_ID_RE.fullmatch(card_id):
            raise ReviewError(400, "invalid_card_id", "Invalid card id.")
        session_dir, raw = self._load_raw(session_id)
        records = self._records_by_card(raw)

        for card in raw.get("memoryCards") or []:
            if isinstance(card, dict) and card.get("cardId") == card_id:
                data, issue = self._locate_screenshot(
                    session_dir, card_id, card.get("screenshotPath"), records.get(card_id), verify_hash=True
                )
                if data:
                    return data
                log.warning("screenshot not served (%s) session=%s card=%s", issue, session_id, card_id)
                break
        raise ReviewError(404, "screenshot_not_available", "Screenshot not available.")

    @staticmethod
    def _records_by_card(raw: Dict[str, Any]) -> Dict[str, Dict[str, Any]]:
        out: Dict[str, Dict[str, Any]] = {}
        for rec in raw.get("screenshots") or []:
            if isinstance(rec, dict) and isinstance(rec.get("cardId"), str):
                out.setdefault(rec["cardId"], rec)
        return out

    # ------------------------------------------------------------ view model

    def load_view(self, session_id: str) -> SessionView:
        session_dir, raw = self._load_raw(session_id)
        records = self._records_by_card(raw)
        issues: List[IssueView] = []

        # ---- memory cards (keep the exported order)
        indexed: List[Tuple[int, int, Dict[str, Any]]] = []
        seen: set = set()
        for index, item in enumerate(raw.get("memoryCards") or []):
            if not isinstance(item, dict):
                issues.append(IssueView(code="card_malformed"))
                continue
            card_id = item.get("cardId")
            if not isinstance(card_id, str) or not CARD_ID_RE.fullmatch(card_id):
                issues.append(IssueView(code="card_id_invalid"))
                continue
            if card_id in seen:
                issues.append(IssueView(code="card_id_duplicate", ref=card_id))
                continue
            seen.add(card_id)
            order_key = _int(item.get("createdOrder"), index)
            indexed.append((order_key, index, item))
        indexed.sort(key=lambda t: (t[0], t[1]))

        cards: List[MemoryCardView] = []
        for position, (_, _, item) in enumerate(indexed):
            card_id = item["cardId"]
            _, issue = self._locate_screenshot(
                session_dir, card_id, item.get("screenshotPath"), records.get(card_id), verify_hash=False
            )
            screenshot: Optional[ScreenshotView] = None
            rel = item.get("screenshotPath")
            if rel not in (None, ""):
                if issue is None:
                    rec = records.get(card_id) or {}
                    screenshot = ScreenshotView(
                        url="/api/sessions/%s/cards/%s/screenshot" % (session_id, card_id),
                        width=_pos_int_or_none(rec.get("width")),
                        height=_pos_int_or_none(rec.get("height")),
                    )
                else:
                    issues.append(IssueView(code=issue, ref=card_id))
            cards.append(
                MemoryCardView(
                    cardId=card_id,
                    order=position,
                    memoryType=_text(item.get("memoryType"), 64),
                    memoryTypeLabel=_text(item.get("memoryTypeLabel"), 64),
                    promptQuestion=_text(item.get("promptQuestion")),
                    userText=_text(item.get("userText")),
                    timeCategory=_text(item.get("timeCategory"), 64),
                    screenshot=screenshot,
                )
            )

        by_id = {c.cardId: c for c in cards}
        groups = self._build_classification(raw, cards, by_id, issues)
        connections = self._build_connections(raw, by_id, issues)

        return SessionView(
            session=SessionMetaView(
                sessionId=session_id,
                schemaVersion=_text(raw.get("schemaVersion"), 20),
                sessionStartedAtUtc=_text(raw.get("sessionStartedAtUtc"), 40),
                exportedAtUtc=_text(raw.get("exportedAtUtc"), 40),
                exportRevision=_int(raw.get("exportRevision"), 0),
            ),
            counts=CountsView(
                memoryCards=len(cards),
                connectionCards=len(connections),
                withScreenshot=sum(1 for c in cards if c.screenshot is not None),
            ),
            memoryCards=cards,
            temporalClassification=groups,
            connectionCards=connections,
            issues=issues,
        )

    @staticmethod
    def _build_classification(
        raw: Dict[str, Any],
        cards: List[MemoryCardView],
        by_id: Dict[str, MemoryCardView],
        issues: List[IssueView],
    ) -> List[ClassificationGroupView]:
        order_of = {c.cardId: c.order for c in cards}
        groups: List[ClassificationGroupView] = []
        placed: set = set()

        for item in raw.get("temporalClassification") or []:
            if not isinstance(item, dict):
                continue
            category = _text(item.get("category"), 64)
            if not category:
                continue
            ids: List[str] = []
            for card_id in item.get("cardIds") or []:
                if not isinstance(card_id, str) or card_id not in by_id:
                    issues.append(IssueView(code="classification_unknown_card", ref=card_id if isinstance(card_id, str) and CARD_ID_RE.fullmatch(card_id) else None))
                    continue
                if card_id in placed:
                    issues.append(IssueView(code="card_in_multiple_groups", ref=card_id))
                    continue
                placed.add(card_id)
                ids.append(card_id)
                if by_id[card_id].timeCategory != category:
                    issues.append(IssueView(code="category_mismatch", ref=card_id))
            groups.append(ClassificationGroupView(category=category, cardIds=ids))

        # Cards that appear in no group: place them by their own timeCategory (never hide them).
        for card in cards:
            if card.cardId in placed:
                continue
            if raw.get("temporalClassification"):
                issues.append(IssueView(code="card_not_classified", ref=card.cardId))
            category = card.timeCategory or "Unclassified"
            target = next((g for g in groups if g.category == category), None)
            if target is None:
                target = ClassificationGroupView(category=category, cardIds=[])
                groups.append(target)
            target.cardIds.append(card.cardId)
            placed.add(card.cardId)

        for group in groups:
            group.cardIds.sort(key=lambda cid: order_of.get(cid, 0))

        # Only reorder when we had to derive everything ourselves.
        if not raw.get("temporalClassification"):
            rank = {name: i for i, name in enumerate(CANONICAL_CATEGORY_ORDER)}
            groups.sort(key=lambda g: rank.get(g.category, len(rank)))
        return groups

    @staticmethod
    def _build_connections(
        raw: Dict[str, Any], by_id: Dict[str, MemoryCardView], issues: List[IssueView]
    ) -> List[ConnectionCardView]:
        out: List[ConnectionCardView] = []
        seen: set = set()
        for index, item in enumerate(raw.get("connectionCards") or []):
            if not isinstance(item, dict):
                issues.append(IssueView(code="connection_malformed"))
                continue
            card_id = item.get("cardId")
            if not isinstance(card_id, str) or not CARD_ID_RE.fullmatch(card_id) or card_id in seen:
                issues.append(IssueView(code="connection_id_invalid"))
                continue
            seen.add(card_id)

            source_id = item.get("sourceCardId")
            source_id = source_id if isinstance(source_id, str) and CARD_ID_RE.fullmatch(source_id) else ""
            source = by_id.get(source_id)
            phase = _text(item.get("sourcePhase"), 16)
            if source is None:
                issues.append(IssueView(code="connection_source_missing", ref=card_id))
            elif phase and source.timeCategory != phase:
                issues.append(IssueView(code="connection_phase_mismatch", ref=card_id))

            color = item.get("groupColor")
            out.append(
                ConnectionCardView(
                    cardId=card_id,
                    order=_int(item.get("createdOrder"), index),
                    sourceCardId=source_id,
                    sourcePhase=phase,
                    sourceFound=source is not None,
                    transitionText=_text(item.get("transitionText")),
                    displayTitle=_text(item.get("displayTitle"), 200),
                    groupColor=color if isinstance(color, str) and COLOR_RE.fullmatch(color) else None,
                )
            )
        out.sort(key=lambda c: c.order)
        return out
