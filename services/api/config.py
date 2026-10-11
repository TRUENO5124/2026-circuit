"""Configuration for the local API service."""

from __future__ import annotations

import os
from pathlib import Path
from dataclasses import dataclass, field


@dataclass
class Config:
    # Server
    host: str = field(default_factory=lambda: os.environ.get("CIRCUIT_API_HOST", "0.0.0.0"))
    port: int = field(default_factory=lambda: int(os.environ.get("CIRCUIT_API_PORT", "3000")))

    # Base directory — override via CIRCUIT_API_DATA_DIR env var (used in tests)
    _data_dir: Path | None = field(default=None, repr=False)

    # Paths — resolved relative to this file
    api_dir: Path = field(default_factory=lambda: Path(__file__).resolve().parent)

    @property
    def project_root(self) -> Path:
        return self.api_dir.parents[1]

    # SQLite database
    @property
    def db_path(self) -> str:
        override = os.environ.get("CIRCUIT_API_DB_PATH")
        if override:
            return override
        return str(self.api_dir / "data" / "api.db")

    # File uploads
    @property
    def upload_dir(self) -> str:
        override = os.environ.get("CIRCUIT_API_UPLOAD_DIR")
        if override:
            return override
        return str(self.api_dir / "data" / "uploads")

    # Benchmark directory
    @property
    def benchmarks_dir(self) -> str:
        return str(self.project_root / "benchmarks" / "examples")

    # Session expiry (seconds)
    session_ttl: int = 300

    # Task queue polling interval (seconds)
    worker_poll_interval: float = 1.0

    # Max upload size (bytes)
    max_upload_size: int = 20 * 1024 * 1024

    def __post_init__(self) -> None:
        os.makedirs(os.path.dirname(self.db_path), exist_ok=True)
        os.makedirs(self.upload_dir, exist_ok=True)


config = Config()
