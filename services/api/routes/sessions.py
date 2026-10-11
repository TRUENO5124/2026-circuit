"""Session routes: create, query, SSE event stream."""

from __future__ import annotations

import asyncio
import json
import time
from datetime import datetime, timezone

from fastapi import APIRouter, HTTPException
from fastapi.responses import StreamingResponse

from ..db import db
from ..config import config
from ..models import SessionCreateResponse, SessionStatusResponse

router = APIRouter(prefix="/api/session", tags=["sessions"])


def _local_ip() -> str:
    """Best-effort local network IP detection."""
    import socket
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except OSError:
        return "127.0.0.1"


@router.post("/create", response_model=SessionCreateResponse)
def create_session() -> SessionCreateResponse:
    session = db.create_session()
    local_ip = _local_ip()
    qr_url = f"http://{local_ip}:{config.port}/capture/{session['id']}?token={session['token']}"
    # Parse ISO datetime safely (UTC-aware)
    try:
        expires_dt = datetime.fromisoformat(session["expires_at"])
    except ValueError:
        expires_dt = datetime.now(timezone.utc)
    remaining = max(0, int((expires_dt - datetime.now(timezone.utc)).total_seconds()))
    return SessionCreateResponse(
        id=session["id"],
        token=session["token"],
        status=session["status"],
        qr_url=qr_url,
        expires_in_seconds=remaining,
    )


@router.get("/{session_id}", response_model=SessionStatusResponse)
def get_session(session_id: str) -> SessionStatusResponse:
    session = db.get_session(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail="Session not found")
    images = db.get_session_images(session_id)
    # Get tasks for this session
    import sqlite3
    rows = db._conn.execute(
        "SELECT * FROM tasks WHERE session_id = ? ORDER BY created_at DESC", (session_id,)
    ).fetchall()
    tasks = [dict(r) for r in rows]
    return SessionStatusResponse(
        id=session["id"],
        status=session["status"],
        image_count=len(images),
        images=images,
        tasks=tasks,
    )


@router.get("/{session_id}/events")
async def session_events(session_id: str):
    """SSE event stream for real-time session updates."""

    async def event_generator():
        session = db.get_session(session_id)
        if session is None:
            yield "event: error\ndata: {\"message\": \"Session not found\"}\n\n"
            return

        last_image_count = len(db.get_session_images(session_id))
        last_status = session["status"]
        timeout_at = time.time() + config.session_ttl

        yield f"event: status\ndata: {json.dumps({'status': last_status})}\n\n"

        while time.time() < timeout_at:
            session = db.get_session(session_id)
            if session is None:
                break

            images = db.get_session_images(session_id)
            current_count = len(images)

            if current_count > last_image_count:
                new_images = images[last_image_count:]
                for img in new_images:
                    yield f"event: upload_complete\ndata: {json.dumps(img, ensure_ascii=False)}\n\n"
                last_image_count = current_count

            current_status = session["status"]
            if current_status != last_status:
                yield f"event: status_change\ndata: {json.dumps({'status': current_status})}\n\n"
                last_status = current_status

            if current_status == "completed":
                break

            await asyncio.sleep(1.0)

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )
