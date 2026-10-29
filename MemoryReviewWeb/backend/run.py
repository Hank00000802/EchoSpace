"""Launcher: python run.py --data-root <dir> --session <SessionId> [--session <SessionId> ...]

The server always binds to 127.0.0.1 (not configurable). Only sessions passed here (or in
ECHOSPACE_REVIEW_SESSIONS) can be opened; there is no session listing endpoint.
"""

from __future__ import annotations

import argparse
import logging
import sys

import uvicorn

from app.config import DATA_ROOT_ENV, DEFAULT_PORT, HOST, SESSIONS_ENV, ConfigError, build_settings
from app.main import create_app


def main() -> int:
    parser = argparse.ArgumentParser(description="EchoSpace local Memory Review server (read-only).")
    parser.add_argument("--data-root", help="Folder that contains 'research/' (default: env %s)" % DATA_ROOT_ENV)
    parser.add_argument(
        "--session",
        action="append",
        default=[],
        metavar="SESSION_ID",
        help="Session the researcher allows for viewing (repeatable; or env %s)" % SESSIONS_ENV,
    )
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")

    try:
        settings = build_settings(args.data_root, args.session)
    except ConfigError as ex:
        print("Config error: %s" % ex, file=sys.stderr)
        return 2

    print("Memory Review (read-only) on http://%s:%d" % (HOST, args.port), flush=True)
    for session_id in sorted(settings.allowed_sessions):
        print("  open: http://%s:%d/?session=%s" % (HOST, args.port, session_id), flush=True)
    if settings.frontend_dist is None or not (settings.frontend_dist / "index.html").is_file():
        print("  note: frontend/dist not found - run 'npm run build' in MemoryReviewWeb/frontend", flush=True)

    uvicorn.run(create_app(settings), host=HOST, port=args.port, log_level="info")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
