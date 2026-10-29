from __future__ import annotations

import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, PlainTextResponse, Response
from fastapi.staticfiles import StaticFiles
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .config import Settings
from .models import SessionView
from .store import ReviewError, SessionStore

log = logging.getLogger("memory_review")

# No inline script/style, no remote origins, no framing. Images only from this origin.
CSP = (
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; "
    "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'"
)


def create_app(settings: Settings) -> FastAPI:
    # Docs / OpenAPI are disabled: smaller surface, nothing to browse.
    app = FastAPI(title="EchoSpace Memory Review", docs_url=None, redoc_url=None, openapi_url=None)
    store = SessionStore(settings)

    # Blocks DNS-rebinding style access (a hostile web page resolving its own name to 127.0.0.1).
    # No CORSMiddleware on purpose: the UI is served from this same origin, so cross-origin
    # pages get no CORS grant and cannot read API responses.
    app.add_middleware(TrustedHostMiddleware, allowed_hosts=["127.0.0.1", "localhost"])

    @app.middleware("http")
    async def security_headers(request: Request, call_next):
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["Referrer-Policy"] = "no-referrer"
        response.headers["Cross-Origin-Resource-Policy"] = "same-origin"
        response.headers["X-Frame-Options"] = "DENY"
        response.headers["Content-Security-Policy"] = CSP
        return response

    @app.exception_handler(ReviewError)
    async def review_error_handler(_: Request, exc: ReviewError):
        return JSONResponse(
            status_code=exc.status, content={"error": {"code": exc.code, "message": exc.message}}
        )

    @app.exception_handler(Exception)
    async def unexpected_error_handler(_: Request, exc: Exception):
        log.exception("unexpected error: %s", type(exc).__name__)
        return JSONResponse(
            status_code=500, content={"error": {"code": "internal_error", "message": "Internal error."}}
        )

    @app.get("/api/health")
    def health():
        return {"status": "ok"}

    @app.get("/api/sessions/{session_id}", response_model=SessionView)
    def get_session(session_id: str):
        return store.load_view(session_id)

    @app.get("/api/sessions/{session_id}/cards/{card_id}/screenshot")
    def get_screenshot(session_id: str, card_id: str):
        return Response(content=store.read_screenshot(session_id, card_id), media_type="image/png")

    dist = settings.frontend_dist
    if dist is not None and (dist / "index.html").is_file():
        app.mount("/", StaticFiles(directory=str(dist), html=True), name="frontend")
    else:

        @app.get("/")
        def frontend_missing():
            return PlainTextResponse(
                "Frontend is not built yet.\n"
                "cd MemoryReviewWeb/frontend && npm install && npm run build\n"
                "(or use 'npm run dev' and open the Vite URL).\n"
            )

    return app
