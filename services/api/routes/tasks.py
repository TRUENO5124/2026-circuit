"""Task routes: submit analysis jobs and check status."""

from __future__ import annotations

import json

from fastapi import APIRouter, HTTPException

from ..db import db
from ..models import (
    TaskSubmitRequest,
    TaskSubmitResponse,
    TaskStatusResponse,
    TaskListResponse,
)

router = APIRouter(prefix="/api/tasks", tags=["tasks"])


def _task_row_to_response(task: dict) -> dict:
    result = None
    if task.get("result_json"):
        try:
            result = json.loads(task["result_json"])
        except json.JSONDecodeError:
            result = {"raw": task["result_json"]}
    return {
        "id": task["id"],
        "kind": task["kind"],
        "status": task["status"],
        "created_at": task["created_at"],
        "started_at": task.get("started_at"),
        "finished_at": task.get("finished_at"),
        "result": result,
        "error": task.get("error"),
    }


@router.post("/submit", response_model=TaskSubmitResponse)
def submit_task(req: TaskSubmitRequest) -> TaskSubmitResponse:
    # Build input JSON from the request
    input_data: dict = {}

    if req.circuit_graph:
        input_data["circuit_graph"] = req.circuit_graph
    elif req.benchmark_id:
        input_data["benchmark_id"] = req.benchmark_id
    else:
        # Try to use the generic input field
        input_data = req.input

    if not input_data:
        raise HTTPException(status_code=400, detail="No input data provided. Send 'circuit_graph' or 'benchmark_id'.")

    task = db.create_task(
        kind=req.kind,
        input_json=json.dumps(input_data, ensure_ascii=False),
        session_id=req.session_id,
    )
    return TaskSubmitResponse(
        id=task["id"],
        kind=task["kind"],
        status=task["status"],
        created_at=task["created_at"],
    )


@router.get("/{task_id}", response_model=TaskStatusResponse)
def get_task(task_id: str) -> TaskStatusResponse:
    task = db.get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail="Task not found")
    return TaskStatusResponse(**_task_row_to_response(task))


@router.get("/", response_model=TaskListResponse)
def list_tasks(limit: int = 50, offset: int = 0) -> TaskListResponse:
    tasks = db.list_tasks(limit=limit, offset=offset)
    return TaskListResponse(
        tasks=[TaskStatusResponse(**_task_row_to_response(t)) for t in tasks],
        total=len(tasks),
    )
