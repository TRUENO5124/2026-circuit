"""SQLite database layer for the local API.

Tables:
  - sessions:  QR-code capture sessions for phone upload
  - tasks:     Analysis jobs queued for the worker
  - images:    Uploaded images linked to sessions
"""

from __future__ import annotations

import os
import sqlite3
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .config import config


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


class Database:
    def __init__(self, db_path: str | None = None):
        path = db_path or config.db_path
        self._path = path
        self._conn = sqlite3.connect(path, check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        self._conn.execute("PRAGMA journal_mode=WAL")
        self._conn.execute("PRAGMA foreign_keys=ON")
        self._migrate()

    # ── schema ──────────────────────────────────────────────────

    def _migrate(self) -> None:
        self._conn.executescript("""
            CREATE TABLE IF NOT EXISTS sessions (
                id          TEXT PRIMARY KEY,
                token       TEXT NOT NULL UNIQUE,
                status      TEXT NOT NULL DEFAULT 'pending',
                created_at  TEXT NOT NULL,
                expires_at  TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS images (
                id          TEXT PRIMARY KEY,
                session_id  TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                filename    TEXT NOT NULL,
                path        TEXT NOT NULL,
                uploaded_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id          TEXT PRIMARY KEY,
                session_id  TEXT REFERENCES sessions(id) ON DELETE SET NULL,
                kind        TEXT NOT NULL DEFAULT 'analyze',
                status      TEXT NOT NULL DEFAULT 'pending',
                input_json  TEXT,
                result_json TEXT,
                error       TEXT,
                created_at  TEXT NOT NULL,
                started_at  TEXT,
                finished_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status);
            CREATE INDEX IF NOT EXISTS idx_tasks_created ON tasks(created_at);
            CREATE INDEX IF NOT EXISTS idx_images_session ON images(session_id);
        """)
        self._conn.commit()

    # ── sessions ─────────────────────────────────────────────────

    def create_session(self) -> dict[str, Any]:
        sid = str(uuid.uuid4())
        token = str(uuid.uuid4()).replace("-", "")
        now = _now()
        expires = datetime.now(timezone.utc).timestamp() + config.session_ttl
        expires_str = datetime.fromtimestamp(expires, tz=timezone.utc).isoformat()

        self._conn.execute(
            "INSERT INTO sessions (id, token, status, created_at, expires_at) VALUES (?, ?, 'pending', ?, ?)",
            (sid, token, now, expires_str),
        )
        self._conn.commit()
        return {"id": sid, "token": token, "status": "pending", "created_at": now, "expires_at": expires_str}

    def get_session(self, sid: str) -> dict[str, Any] | None:
        row = self._conn.execute("SELECT * FROM sessions WHERE id = ?", (sid,)).fetchone()
        return dict(row) if row else None

    def update_session_status(self, sid: str, status: str) -> None:
        self._conn.execute("UPDATE sessions SET status = ? WHERE id = ?", (status, sid))
        self._conn.commit()

    # ── images ───────────────────────────────────────────────────

    def add_image(self, session_id: str, filename: str, filepath: str) -> dict[str, Any]:
        iid = str(uuid.uuid4())
        now = _now()
        self._conn.execute(
            "INSERT INTO images (id, session_id, filename, path, uploaded_at) VALUES (?,?,?,?,?)",
            (iid, session_id, filename, filepath, now),
        )
        self._conn.commit()
        self.update_session_status(session_id, "linked")
        return {"id": iid, "session_id": session_id, "filename": filename, "path": filepath, "uploaded_at": now}

    def get_session_images(self, session_id: str) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            "SELECT * FROM images WHERE session_id = ? ORDER BY uploaded_at", (session_id,)
        ).fetchall()
        return [dict(r) for r in rows]

    # ── tasks ────────────────────────────────────────────────────

    def create_task(self, kind: str = "analyze", input_json: str = "{}", session_id: str | None = None) -> dict[str, Any]:
        tid = str(uuid.uuid4())
        now = _now()
        self._conn.execute(
            "INSERT INTO tasks (id, session_id, kind, status, input_json, created_at) VALUES (?,?,?,'pending',?,?)",
            (tid, session_id, kind, input_json, now),
        )
        self._conn.commit()
        return {"id": tid, "kind": kind, "status": "pending", "created_at": now}

    def get_task(self, tid: str) -> dict[str, Any] | None:
        row = self._conn.execute("SELECT * FROM tasks WHERE id = ?", (tid,)).fetchone()
        return dict(row) if row else None

    def get_next_pending_task(self) -> dict[str, Any] | None:
        """Atomically dequeue the oldest pending task and mark it running.

        Uses UPDATE … RETURNING (SQLite 3.35+) to avoid a SELECT/UPDATE race.
        """
        now = _now()
        row = self._conn.execute(
            """UPDATE tasks SET status = 'running', started_at = ?
               WHERE id = (
                 SELECT id FROM tasks
                 WHERE status = 'pending'
                 ORDER BY created_at ASC LIMIT 1
               )
               RETURNING *""",
            (now,),
        ).fetchone()
        self._conn.commit()
        return dict(row) if row else None

    def complete_task(self, tid: str, result_json: str) -> None:
        now = _now()
        self._conn.execute(
            "UPDATE tasks SET status = 'completed', result_json = ?, finished_at = ? WHERE id = ?",
            (result_json, now, tid),
        )
        self._conn.commit()

    def fail_task(self, tid: str, error: str) -> None:
        now = _now()
        self._conn.execute(
            "UPDATE tasks SET status = 'failed', error = ?, finished_at = ? WHERE id = ?",
            (error, now, tid),
        )
        self._conn.commit()

    def list_tasks(self, limit: int = 50, offset: int = 0) -> list[dict[str, Any]]:
        rows = self._conn.execute(
            "SELECT * FROM tasks ORDER BY created_at DESC LIMIT ? OFFSET ?", (limit, offset)
        ).fetchall()
        return [dict(r) for r in rows]

    # ── cleanup ──────────────────────────────────────────────────

    def purge_expired_sessions(self) -> int:
        now = _now()
        cur = self._conn.execute("DELETE FROM sessions WHERE expires_at < ?", (now,))
        self._conn.commit()
        return cur.rowcount


# Singleton — respects CIRCUIT_API_DB_PATH env var for testing
_DB_PATH = os.environ.get("CIRCUIT_API_DB_PATH", config.db_path)
db = Database(_DB_PATH)
