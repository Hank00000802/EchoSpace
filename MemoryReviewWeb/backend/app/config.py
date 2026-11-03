"""Runtime settings for the local Memory Review server.

Everything here is decided by the researcher at launch time (CLI flags or environment
variables). Nothing is read from the browser, and the server never lists sessions.
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional

DATA_ROOT_ENV = "ECHOSPACE_DATA_DIR"  # same variable Unity's SessionExportManager uses
SESSIONS_ENV = "ECHOSPACE_REVIEW_SESSIONS"  # comma / semicolon / space separated

# Same rule as Unity's SessionContext.IsValidSessionId (^[A-Za-z0-9_-]{8,40}$).
SESSION_ID_RE = re.compile(r"[A-Za-z0-9_-]{8,40}")

HOST = "127.0.0.1"  # fixed on purpose: this server is never exposed to the network
DEFAULT_PORT = 8765  # 8000 is used by tools/stt_proxy

# MemoryReviewWeb/backend/app/config.py -> MemoryReviewWeb/
WEB_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_FRONTEND_DIST = WEB_ROOT / "frontend" / "dist"


class ConfigError(Exception):
    """Raised for invalid launch configuration (shown to the researcher on the console)."""


@dataclass(frozen=True)
class Settings:
    data_root: Path
    allowed_sessions: frozenset
    frontend_dist: Optional[Path] = DEFAULT_FRONTEND_DIST

    @property
    def research_root(self) -> Path:
        return self.data_root / "research"

    @property
    def review_root(self) -> Path:
        # Overlay only (trash). Never mix with Unity's research/session.json.
        return self.data_root / "review"


def unity_default_data_root() -> Optional[Path]:
    """Unity's fallback: Application.persistentDataPath/EchoSpaceData (Windows only here)."""
    base = os.environ.get("USERPROFILE")
    if not base:
        return None
    return (
        Path(base)
        / "AppData"
        / "LocalLow"
        / "DefaultCompany"  # ProjectSettings companyName
        / "EchoSpace_2022_3D_Builtin"  # ProjectSettings productName
        / "EchoSpaceData"
    )


def parse_session_ids(values: Iterable[str]) -> frozenset:
    result = set()
    for value in values:
        for token in re.split(r"[,;\s]+", value or ""):
            token = token.strip()
            if not token:
                continue
            if not SESSION_ID_RE.fullmatch(token):
                raise ConfigError("Invalid session id: %r (expected 8-40 chars of A-Z a-z 0-9 _ -)" % token)
            result.add(token)
    return frozenset(result)


def _git_ancestor(path: Path) -> Optional[Path]:
    for candidate in [path, *path.parents]:
        if (candidate / ".git").exists():
            return candidate
    return None


def build_settings(
    data_root: Optional[str],
    sessions: Iterable[str],
    frontend_dist: Optional[Path] = DEFAULT_FRONTEND_DIST,
) -> Settings:
    raw_root = data_root or os.environ.get(DATA_ROOT_ENV)
    root = Path(raw_root) if raw_root else unity_default_data_root()
    if root is None:
        raise ConfigError("dataRoot not set. Use --data-root or set %s." % DATA_ROOT_ENV)

    root = root.expanduser().resolve()
    if not (root / "research").is_dir():
        raise ConfigError("'research' folder not found under data root: %s" % root)

    repo = _git_ancestor(root)
    if repo is not None:
        raise ConfigError(
            "Data root is inside a Git repository (%s). Research data must stay outside the repo." % repo
        )

    allowed = parse_session_ids([*sessions, os.environ.get(SESSIONS_ENV, "")])
    if not allowed:
        raise ConfigError(
            "No session allowed. Pass --session <SessionId> (repeatable) or set %s." % SESSIONS_ENV
        )

    return Settings(data_root=root, allowed_sessions=allowed, frontend_dist=frontend_dist)
