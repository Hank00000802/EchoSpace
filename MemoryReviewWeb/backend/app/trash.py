"""Participant trash overlay. Soft-delete only: Unity session.json / PNG / revisions are never touched.

Stored at {dataRoot}/review/{sessionId}/trash.json (outside research/). The researcher still has
the original export; the web merely hides cards that the participant moved to the trash.
"""

from __future__ import annotations

import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Literal, Tuple

from .models import SessionView, TrashItemView
from .store import CARD_ID_RE, MAX_JSON_BYTES, ReviewError, SessionStore

log = logging.getLogger("memory_review")

Kind = Literal["memory", "connection"]
MAX_TRASH_ITEMS = 400


class TrashOverlay:
    def __init__(self, review_root: Path, sessions: SessionStore):
        self._review_root = review_root
        self._sessions = sessions

    def attach(self, view: SessionView) -> SessionView:
        items = self.list_items(view.session.sessionId)
        view.trash = items
        view.counts.trashed = len(items)
        return view

    def move(self, session_id: str, kind: Kind, card_id: str) -> SessionView:
        view = self._sessions.load_view(session_id)
        self._require_card(view, kind, card_id)
        items = self._read(session_id)
        if not any(i.kind == kind and i.cardId == card_id for i in items):
            if len(items) >= MAX_TRASH_ITEMS:
                raise ReviewError(422, "trash_full", "Trash is full.")
            items.append(
                TrashItemView(
                    kind=kind,
                    cardId=card_id,
                    deletedAtUtc=datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                )
            )
            self._write(session_id, items)
        view.trash = items
        view.counts.trashed = len(items)
        return view

    def restore(self, session_id: str, kind: Kind, card_id: str) -> SessionView:
        view = self._sessions.load_view(session_id)
        items = [i for i in self._read(session_id) if not (i.kind == kind and i.cardId == card_id)]
        self._write(session_id, items)
        view.trash = items
        view.counts.trashed = len(items)
        return view

    def list_items(self, session_id: str) -> List[TrashItemView]:
        return self._read(session_id)

    def _require_card(self, view: SessionView, kind: Kind, card_id: str) -> None:
        if not CARD_ID_RE.fullmatch(card_id):
            raise ReviewError(400, "invalid_card_id", "Invalid card id.")
        found = (
            any(c.cardId == card_id for c in view.memoryCards)
            if kind == "memory"
            else any(c.cardId == card_id for c in view.connectionCards)
        )
        if not found:
            raise ReviewError(404, "card_not_available", "Card not available.")

    def _folder(self, session_id: str, create: bool) -> Path | None:
        self._sessions._session_dir(session_id)
        try:
            if create:
                self._review_root.mkdir(parents=True, exist_ok=True)
            if not self._review_root.exists():
                return None
            root = self._review_root.resolve()
        except OSError as ex:
            if not create:
                return None
            log.warning("cannot create review root: %s", ex)
            raise ReviewError(500, "trash_write_failed", "Could not save trash.")
        target = (root / session_id).resolve()
        if target.parent != root:
            raise ReviewError(400, "invalid_session_id", "Invalid session id.")
        if create:
            try:
                target.mkdir(parents=True, exist_ok=True)
            except OSError as ex:
                log.warning("cannot create review folder: %s", ex)
                raise ReviewError(500, "trash_write_failed", "Could not save trash.")
        elif not target.is_dir():
            return None
        return target

    def _read(self, session_id: str) -> List[TrashItemView]:
        folder = self._folder(session_id, create=False)
        if folder is None:
            return []
        path = folder / "trash.json"
        if path.is_symlink() or not path.is_file():
            return []
        try:
            if path.stat().st_size > MAX_JSON_BYTES:
                raise ReviewError(422, "trash_unreadable", "Trash file is too large.")
            raw = json.loads(path.read_bytes().decode("utf-8-sig"))
        except ReviewError:
            raise
        except (OSError, UnicodeDecodeError, ValueError) as ex:
            log.warning("cannot read trash.json for %s: %s", session_id, ex)
            raise ReviewError(422, "trash_unreadable", "Trash file could not be read.")
        return _parse_items(raw, session_id)

    def _write(self, session_id: str, items: List[TrashItemView]) -> None:
        folder = self._folder(session_id, create=True)
        if folder is None:
            raise ReviewError(500, "trash_write_failed", "Could not save trash.")
        path = folder / "trash.json"
        payload = json.dumps(
            {
                "sessionId": session_id,
                "items": [i.model_dump() for i in items],
            },
            ensure_ascii=False,
            indent=2,
        ).encode("utf-8")
        tmp = folder / "trash.json.tmp"
        try:
            with tmp.open("wb") as fh:
                fh.write(payload)
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(str(tmp), str(path))
        except OSError as ex:
            log.warning("cannot write trash.json for %s: %s", session_id, ex)
            try:
                tmp.unlink(missing_ok=True)
            except OSError:
                pass
            raise ReviewError(500, "trash_write_failed", "Could not save trash.")


def _parse_items(raw: object, session_id: str) -> List[TrashItemView]:
    if not isinstance(raw, dict):
        return []
    if raw.get("sessionId") not in (None, "", session_id):
        log.warning("trash.json sessionId mismatch for %s", session_id)
        raise ReviewError(422, "trash_unreadable", "Trash file does not belong to this session.")
    out: List[TrashItemView] = []
    seen: set = set()
    for item in raw.get("items") or []:
        if not isinstance(item, dict):
            continue
        kind = item.get("kind")
        card_id = item.get("cardId")
        if kind not in ("memory", "connection"):
            continue
        if not isinstance(card_id, str) or not CARD_ID_RE.fullmatch(card_id):
            continue
        key: Tuple[str, str] = (kind, card_id)
        if key in seen:
            continue
        seen.add(key)
        when = item.get("deletedAtUtc")
        when = when if isinstance(when, str) else ""
        out.append(TrashItemView(kind=kind, cardId=card_id, deletedAtUtc=when[:40]))
        if len(out) >= MAX_TRASH_ITEMS:
            break
    return out
