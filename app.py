import hashlib
import hmac
import json
import logging
import os
import time
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import httpx
from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, Response
from fastapi.security import HTTPBasic, HTTPBasicCredentials
from fastapi.staticfiles import StaticFiles
ROOT = Path(__file__).resolve().parent
STATIC_DIR = ROOT / "static"
WEBEX_API = "https://webexapis.com/v1"
GEMINI_API = "https://generativelanguage.googleapis.com/v1beta"
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-3.8-flash")
WEBEX_SECRET = (
    os.getenv("WEBEX_WEBHOOK_SECRET") or os.getenv("WEBEX_SECRET") or ""
).strip()
WEBEX_TOKEN = os.getenv("WEBEX_BOT_TOKEN", "").strip()
GOOGLE_API_KEY = os.getenv("GOOGLE_API_KEY", "").strip()
DASHBOARD_PASSWORD = os.getenv("DASHBOARD_PASSWORD", "")

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("webex_gemini")
events: deque[dict[str, str]] = deque(maxlen=40)
started_at = time.time()
bot_person_id: str | None = None

app = FastAPI(title="Webex Gemini Control")
app.mount("/static", StaticFiles(directory=STATIC_DIR, check_dir=False), name="static")
dashboard_security = HTTPBasic()


def record_event(kind: str, detail: str, status: str = "info") -> None:
    event = {
        "time": datetime.now(timezone.utc).isoformat(),
        "kind": kind,
        "detail": detail,
        "status": status,
    }
    events.appendleft(event)
    logger.info("%s | %s | %s", status.upper(), kind, detail)


def webhook_signature_is_valid(body: bytes, signature: str) -> bool:
    if not WEBEX_SECRET or not signature:
        return False
    digest = hmac.new(WEBEX_SECRET.encode("utf-8"), body, hashlib.sha1).hexdigest()
    return hmac.compare_digest(digest, signature.strip().lower())


def require_dashboard_auth(credentials: HTTPBasicCredentials = Depends(dashboard_security)) -> None:
    if not DASHBOARD_PASSWORD:
        raise HTTPException(status_code=503, detail="Set DASHBOARD_PASSWORD in the service environment.")
    valid_user = hmac.compare_digest(credentials.username.encode(), b"admin")
    valid_password = hmac.compare_digest(credentials.password, DASHBOARD_PASSWORD)
    if not (valid_user and valid_password):
        raise HTTPException(
            status_code=401,
            detail="Incorrect dashboard credentials.",
            headers={"WWW-Authenticate": "Basic"},
        )


def extract_gemini_text(payload: dict[str, Any]) -> str:
    for candidate in payload.get("candidates", []):
        content = candidate.get("content", {})
        for part in content.get("parts", []):
            if isinstance(part.get("text"), str):
                return part["text"]
    return ""


async def gemini_generate(parts: list[dict[str, Any]]) -> str:
    if not GOOGLE_API_KEY:
        raise HTTPException(status_code=503, detail="GOOGLE_API_KEY is not configured.")
    url = f"{GEMINI_API}/models/{GEMINI_MODEL}:generateContent"
    async with httpx.AsyncClient(timeout=90) as client:
        response = await client.post(
            url,
            headers={"x-goog-api-key": GOOGLE_API_KEY},
            json={"contents": [{"parts": parts}]},
        )
    if response.is_error:
        logger.error("Gemini text request failed with HTTP %s.", response.status_code)
        raise HTTPException(status_code=502, detail="Gemini text request failed.")
    text = extract_gemini_text(response.json())
    if not text:
        raise HTTPException(status_code=502, detail="Gemini returned no text.")
    return text


async def gemini_text(prompt: str) -> str:
    return await gemini_generate([{"text": prompt}])


async def get_webex_message(message_id: str) -> str | None:
    headers = {"Authorization": f"Bearer {WEBEX_TOKEN}"}
    async with httpx.AsyncClient(timeout=45) as client:
        response = await client.get(f"{WEBEX_API}/messages/{message_id}", headers=headers)
        response.raise_for_status()
        return response.json().get("text")


async def get_bot_person_id() -> str | None:
    global bot_person_id
    if bot_person_id or not WEBEX_TOKEN:
        return bot_person_id
    async with httpx.AsyncClient(timeout=20) as client:
        response = await client.get(
            f"{WEBEX_API}/people/me",
            headers={"Authorization": f"Bearer {WEBEX_TOKEN}"},
        )
    if response.is_error:
        logger.error("Could not verify Webex bot identity: HTTP %s.", response.status_code)
        return None
    bot_person_id = response.json().get("id")
    return bot_person_id


async def send_webex_message(room_id: str, text: str) -> None:
    async with httpx.AsyncClient(timeout=30) as client:
        response = await client.post(
            f"{WEBEX_API}/messages",
            headers={"Authorization": f"Bearer {WEBEX_TOKEN}"},
            json={"roomId": room_id, "text": text},
        )
    if response.is_error:
        logger.error("Webex reply failed with HTTP %s.", response.status_code)
        response.raise_for_status()


@app.get("/")
async def dashboard(_: None = Depends(require_dashboard_auth)) -> FileResponse:
    return FileResponse(STATIC_DIR / "index.html")


@app.head("/")
async def dashboard_probe() -> Response:
    return Response(status_code=200)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "healthy"}


@app.get("/api/status")
async def status(_: None = Depends(require_dashboard_auth)) -> dict[str, Any]:
    settings = {
        "webexBotToken": bool(WEBEX_TOKEN),
        "webhookSecret": bool(WEBEX_SECRET),
        "googleApiKey": bool(GOOGLE_API_KEY),
    }
    return {
        "state": "ready" if all(settings.values()) else "needs-configuration",
        "uptimeSeconds": int(time.time() - started_at),
        "webhookEvents": len(events),
        "settings": settings,
        "models": {"chat": GEMINI_MODEL},
    }


@app.get("/api/events")
async def recent_events(_: None = Depends(require_dashboard_auth)) -> list[dict[str, str]]:
    return list(events)


@app.post("/api/gemini/check")
async def check_gemini(_: None = Depends(require_dashboard_auth)) -> dict[str, str]:
    try:
        result = await gemini_text("Reply with exactly: OK")
    except HTTPException:
        record_event("Gemini check", "Connection test failed; inspect service logs.", "error")
        raise
    record_event("Gemini check", "Gemini API responded successfully.", "success")
    return {"status": "connected", "reply": result}


@app.post("/webhook")
async def webhook(request: Request) -> dict[str, bool]:
    body = await request.body()
    signature = request.headers.get("X-Spark-Signature", "")
    logger.info(
        "Webhook received. BodyLength=%d, SignaturePresent=%s",
        len(body),
        bool(signature.strip()),
    )
    if not webhook_signature_is_valid(body, signature):
        detail = "Signature missing" if not signature.strip() else "Signature invalid"
        record_event("Webhook rejected", detail, "error")
        logger.warning("Webhook rejected: %s.", detail)
        raise HTTPException(status_code=401, detail="Invalid webhook signature.")

    try:
        payload = __import__("json").loads(body)
    except ValueError as exc:
        record_event("Webhook rejected", "Invalid JSON payload", "error")
        raise HTTPException(status_code=400, detail="Invalid JSON payload.") from exc

    resource = payload.get("resource", "unknown")
    event_type = payload.get("event", "unknown")
    record_event("Webhook accepted", f"{resource} / {event_type}", "success")
    if resource != "messages":
        return {"ok": True}

    data = payload.get("data", {})
    message_id = data.get("id")
    room_id = data.get("roomId")
    if not message_id or not room_id:
        record_event("Message ignored", "Missing message or room ID", "warning")
        return {"ok": True}

    own_id = await get_bot_person_id()
    if own_id and data.get("personId") == own_id:
        record_event("Message ignored", "Sender is this bot", "info")
        return {"ok": True}

    try:
        text = (await get_webex_message(message_id) or "").strip()
        if text:
            reply = await gemini_text(text)
            record_event("Text processed", "Gemini generated a reply", "success")
        else:
            record_event("Message ignored", "No text content", "warning")
            return {"ok": True}
        await send_webex_message(room_id, reply)
        record_event("Reply sent", "Webex message delivered", "success")
    except httpx.HTTPError as exc:
        record_event("Message failed", f"External API request failed: {type(exc).__name__}", "error")
        raise HTTPException(status_code=502, detail="A Webex or Gemini request failed.") from exc
    except HTTPException as exc:
        record_event("Message failed", exc.detail, "error")
        raise

    return {"ok": True}