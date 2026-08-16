import os
import tempfile
from pathlib import Path

from dotenv import load_dotenv
from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from openai import OpenAI

ENV_PATH = Path(__file__).resolve().parent / ".env"


def _load_local_env() -> None:
    if not ENV_PATH.exists():
        return

    for encoding in ("utf-8", "utf-8-sig", "utf-16", "utf-16-le"):
        load_dotenv(ENV_PATH, encoding=encoding, override=True)
        if os.getenv("OPENAI_API_KEY"):
            return

    # Fallback for Windows-created .env files that python-dotenv may fail to parse.
    for encoding in ("utf-8", "utf-8-sig", "utf-16", "utf-16-le"):
        try:
            text = ENV_PATH.read_text(encoding=encoding)
        except UnicodeError:
            continue

        for line in text.splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, _, value = line.partition("=")
            key = key.strip()
            value = value.strip().strip('"').strip("'")
            if key and value:
                os.environ[key] = value

        if os.getenv("OPENAI_API_KEY"):
            return


_load_local_env()

TRANSCRIPTION_PROMPT = (
    "This audio is a VR memory annotation from EchoSpace. "
    "The speaker may use Traditional Chinese, English, or a mixed "
    "Chinese-English code-switching style. Transcribe faithfully and "
    "preserve the original language usage."
)

app = FastAPI(title="EchoSpace STT Proxy")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_client: OpenAI | None = None


def _get_settings() -> tuple[str | None, str]:
    """Reload .env so edits take effect after server restart, and for health checks."""
    _load_local_env()
    api_key = os.getenv("OPENAI_API_KEY")
    model = os.getenv("STT_MODEL", "gpt-4o-mini-transcribe")
    return api_key, model


def _get_client() -> OpenAI | None:
    global _client
    api_key, _ = _get_settings()
    if not api_key:
        _client = None
        return None
    if _client is None:
        _client = OpenAI(api_key=api_key)
    return _client


@app.get("/health")
def health():
    api_key, model = _get_settings()
    return {
        "status": "ok" if api_key else "missing_api_key",
        "model": model,
    }


@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    client = _get_client()
    if client is None:
        raise HTTPException(
            status_code=500,
            detail="OPENAI_API_KEY is not configured. Set it in tools/stt_proxy/.env",
        )

    _, stt_model = _get_settings()
    filename = file.filename or "audio.wav"
    suffix = Path(filename).suffix or ".wav"

    tmp_path = None
    try:
        contents = await file.read()
        if not contents:
            raise HTTPException(status_code=400, detail="Uploaded file is empty")

        with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
            tmp.write(contents)
            tmp_path = tmp.name

        with open(tmp_path, "rb") as audio_file:
            result = client.audio.transcriptions.create(
                model=stt_model,
                file=audio_file,
                prompt=TRANSCRIPTION_PROMPT,
            )

        text = result.text if hasattr(result, "text") else str(result)
        return {"text": text.strip()}
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Transcription failed: {exc}") from exc
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.remove(tmp_path)
