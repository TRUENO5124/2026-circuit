"""Pydantic models for API request/response schemas."""

from __future__ import annotations

from typing import Any, Optional
from pydantic import BaseModel, Field


# ── Session ──────────────────────────────────────────────────────

class SessionCreateResponse(BaseModel):
    id: str
    token: str
    status: str
    qr_url: str = ""       # filled by the route handler
    expires_in_seconds: int


class SessionStatusResponse(BaseModel):
    id: str
    status: str
    image_count: int
    images: list[dict[str, Any]] = []
    tasks: list[dict[str, Any]] = []


# ── Task ─────────────────────────────────────────────────────────

class TaskSubmitRequest(BaseModel):
    kind: str = "analyze"
    session_id: str | None = None
    input: dict[str, Any] = Field(default_factory=dict)
    # Fields for analysis tasks:
    benchmark_id: str | None = None     # load from benchmarks/examples/
    circuit_graph: dict[str, Any] | None = None  # inline CircuitGraph


class TaskSubmitResponse(BaseModel):
    id: str
    kind: str
    status: str
    created_at: str


class TaskStatusResponse(BaseModel):
    id: str
    kind: str
    status: str
    created_at: str
    started_at: str | None = None
    finished_at: str | None = None
    result: dict[str, Any] | None = None
    error: str | None = None


class TaskListResponse(BaseModel):
    tasks: list[TaskStatusResponse]
    total: int


# ── Upload ───────────────────────────────────────────────────────

class UploadResponse(BaseModel):
    id: str
    filename: str
    session_id: str
    uploaded_at: str


# ── Health ───────────────────────────────────────────────────────

class HealthResponse(BaseModel):
    status: str = "ok"
    version: str = "0.1.0"
    task_count: int = 0
