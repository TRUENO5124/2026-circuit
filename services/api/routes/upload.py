"""Image upload route for phone capture."""

from __future__ import annotations

import os
import uuid
from pathlib import Path

from fastapi import APIRouter, File, Header, HTTPException, Query, UploadFile

from ..config import config
from ..db import db
from ..models import UploadResponse

router = APIRouter(prefix="/api", tags=["upload"])

# Allowed image extensions
_ALLOWED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}


def _safe_filename(original: str) -> str:
    """Generate a safe, unique filename."""
    ext = Path(original).suffix.lower()
    if ext not in _ALLOWED_EXTENSIONS:
        ext = ".jpg"
    return f"{uuid.uuid4().hex}{ext}"


@router.post("/session/{session_id}/upload", response_model=UploadResponse)
async def upload_image(
    session_id: str,
    image: UploadFile = File(...),
    authorization: str | None = Header(None),
    token: str | None = Query(None),
) -> UploadResponse:
    # Auth: token as Bearer header or query param
    auth_token = None
    if authorization and authorization.startswith("Bearer "):
        auth_token = authorization[7:]
    elif token:
        auth_token = token

    # Verify session exists
    session = db.get_session(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail="Session not found")

    # Token required
    if not auth_token:
        raise HTTPException(status_code=403, detail="Token required")

    if auth_token != session["token"]:
        raise HTTPException(status_code=403, detail="Invalid token")

    # Validate file
    if image.filename is None:
        raise HTTPException(status_code=400, detail="No filename")

    safe_name = _safe_filename(image.filename)
    filepath = os.path.join(config.upload_dir, safe_name)

    # Read and save
    content = await image.read()
    if len(content) > config.max_upload_size:
        raise HTTPException(status_code=413, detail=f"File too large (max {config.max_upload_size // 1024 // 1024} MB)")

    with open(filepath, "wb") as f:
        f.write(content)

    # Record in DB
    img = db.add_image(session_id, image.filename, filepath)

    return UploadResponse(
        id=img["id"],
        filename=img["filename"],
        session_id=img["session_id"],
        uploaded_at=img["uploaded_at"],
    )
