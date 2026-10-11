"""Background worker that polls the task queue and runs analysis jobs."""

from __future__ import annotations

import asyncio
import json
import sys
import threading
import time
from pathlib import Path

# Ensure the project root is importable for engine
_PROJECT_ROOT = Path(__file__).resolve().parents[2]
if str(_PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(_PROJECT_ROOT))

from .db import db
from .config import config


class TaskWorker:
    """Polls tasks from SQLite and dispatches them to the analysis engine.

    Runs in a background daemon thread so it doesn't block the FastAPI event loop.
    Communicates results back through the shared SQLite database (db.py singleton).
    """

    def __init__(self) -> None:
        self._running = False
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="task-worker")
        self._thread.start()

    def stop(self) -> None:
        self._running = False
        if self._thread:
            self._thread.join(timeout=5.0)

    def _loop(self) -> None:
        while self._running:
            task = db.get_next_pending_task()
            if task is None:
                time.sleep(config.worker_poll_interval)
                continue

            try:
                result = self._dispatch(task)
                db.complete_task(task["id"], json.dumps(result, ensure_ascii=False))
            except Exception as exc:
                db.fail_task(task["id"], f"{type(exc).__name__}: {exc}")

    def _dispatch(self, task: dict) -> dict:
        kind = task.get("kind", "analyze")
        input_data = json.loads(task["input_json"]) if task.get("input_json") else {}

        if kind == "analyze":
            return self._run_analysis(input_data)
        elif kind == "validate":
            return self._run_validation(input_data)
        else:
            raise ValueError(f"Unknown task kind: {kind}")

    def _run_analysis(self, input_data: dict) -> dict:
        """Run the analysis engine on a circuit graph."""
        from services.engine.src.circuit_loader import parse_circuit_graph
        from services.engine.src.pipeline import AnalysisPipeline, UnsupportedCircuitError

        # Case 1: Inline circuit graph
        if "circuit_graph" in input_data:
            graph = parse_circuit_graph(input_data["circuit_graph"])
        # Case 2: Benchmark by id
        elif "benchmark_id" in input_data:
            bm_path = Path(config.benchmarks_dir) / input_data["benchmark_id"]
            if not bm_path.exists():
                raise FileNotFoundError(f"Benchmark not found: {bm_path}")
            with open(bm_path, encoding="utf-8") as f:
                bm_raw = json.load(f)
            graph = parse_circuit_graph(bm_raw["circuitGraph"])
        else:
            raise ValueError("Task input must contain 'circuit_graph' or 'benchmark_id'")

        pipeline = AnalysisPipeline()
        try:
            result = pipeline.analyze(graph)
        except UnsupportedCircuitError as e:
            return {
                "problemType": "unsupported",
                "error": str(e),
                "steps": [],
                "warnings": [str(e)],
                "isSymbolic": False,
            }

        return result.to_dict()

    def _run_validation(self, input_data: dict) -> dict:
        """Validate a circuit graph against the component definitions."""
        from services.engine.src.circuit_loader import parse_circuit_graph
        from services.engine.src.component_defs import COMPONENT_DEFINITIONS
        from services.engine.src.types import ValidationIssue

        graph = parse_circuit_graph(input_data["circuit_graph"])

        # Simple validation — check component definition references
        issues: list[dict] = []
        defs_by_id = {d.id: d for d in COMPONENT_DEFINITIONS}

        for comp in graph.components:
            if comp.definition_id not in defs_by_id:
                issues.append({
                    "severity": "error",
                    "code": "unknown_component_definition",
                    "message": f"Unknown definition: {comp.definition_id}",
                    "path": ["components", comp.id],
                })

        return {
            "valid": len(issues) == 0,
            "issue_count": len(issues),
            "issues": issues,
        }


# Singleton
worker = TaskWorker()
