"""Integration tests for the local API."""

from __future__ import annotations

import io
import json
import os
import sys
import tempfile
import time
from pathlib import Path

# ── Temp dirs BEFORE any service.api imports ─────────────────────
_TMPDIR = tempfile.mkdtemp(prefix="circuit_api_test_")
_UPLOAD_DIR = os.path.join(_TMPDIR, "uploads")
os.makedirs(_UPLOAD_DIR, exist_ok=True)
os.environ["CIRCUIT_API_DB_PATH"] = os.path.join(_TMPDIR, "test.db")
os.environ["CIRCUIT_API_UPLOAD_DIR"] = _UPLOAD_DIR

_PROJECT_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(_PROJECT_ROOT))

for _key in list(sys.modules.keys()):
    if _key.startswith("services.api"):
        del sys.modules[_key]

from services.api.main import app
from fastapi.testclient import TestClient

client = TestClient(app)


def _create_test_png() -> tuple[bytes, str]:
    import struct, zlib
    def _chunk(ctype: bytes, data: bytes) -> bytes:
        c = ctype + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)
    ihdr = struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)
    raw = b"\x00\xff\x00\x00"
    png = (b"\x89PNG\r\n\x1a\n" + _chunk(b"IHDR", ihdr)
           + _chunk(b"IDAT", zlib.compress(raw)) + _chunk(b"IEND", b""))
    return png, "test.png"


# ── Sync endpoint tests ──────────────────────────────────────────

def test_health() -> None:
    r = client.get("/api/health")
    assert r.status_code == 200
    assert r.json()["status"] == "ok"


def test_create_and_get_session() -> None:
    r = client.post("/api/session/create")
    assert r.status_code == 200
    d = r.json()
    assert d["id"] and d["token"] and d["qr_url"]
    assert d["expires_in_seconds"] > 0

    r2 = client.get(f"/api/session/{d['id']}")
    assert r2.status_code == 200
    assert r2.json()["status"] == "pending"


def test_session_404() -> None:
    assert client.get("/api/session/nonexistent").status_code == 404


def test_image_upload() -> None:
    r = client.post("/api/session/create")
    sid = r.json()["id"]
    token = r.json()["token"]

    png, fname = _create_test_png()
    r2 = client.post(
        f"/api/session/{sid}/upload?token={token}",
        files={"image": (fname, io.BytesIO(png), "image/png")},
    )
    assert r2.status_code == 200
    assert r2.json()["filename"] == fname
    assert client.get(f"/api/session/{sid}").json()["status"] == "linked"


def test_upload_rejected_no_token() -> None:
    r = client.post("/api/session/create")
    sid = r.json()["id"]
    png, fname = _create_test_png()
    r2 = client.post(
        f"/api/session/{sid}/upload",
        files={"image": (fname, io.BytesIO(png), "image/png")},
    )
    assert r2.status_code == 403


def test_upload_rejected_wrong_token() -> None:
    r = client.post("/api/session/create")
    sid = r.json()["id"]
    png, fname = _create_test_png()
    r2 = client.post(
        f"/api/session/{sid}/upload?token=wrong-token",
        files={"image": (fname, io.BytesIO(png), "image/png")},
    )
    assert r2.status_code == 403


def test_submit_task_by_benchmark() -> None:
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "analyze", "benchmark_id": "bjt-common-emitter-bias.json"},
    )
    assert r.status_code == 200
    payload = r.json()
    assert payload["status"] == "pending"
    assert payload["id"]
    assert "task_id" not in payload


def test_submit_task_inline() -> None:
    bm_path = _PROJECT_ROOT / "benchmarks" / "examples" / "diode-limiter.json"
    with open(bm_path) as f:
        bm = json.load(f)
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "analyze", "circuit_graph": bm["circuitGraph"]},
    )
    assert r.status_code == 200


def test_submit_task_validate() -> None:
    bm_path = _PROJECT_ROOT / "benchmarks" / "examples" / "diode-limiter.json"
    with open(bm_path) as f:
        bm = json.load(f)
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "validate", "circuit_graph": bm["circuitGraph"]},
    )
    assert r.status_code == 200


def test_get_task() -> None:
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "analyze", "benchmark_id": "bjt-common-emitter-bias.json"},
    )
    tid = r.json()["id"]
    r2 = client.get(f"/api/tasks/{tid}")
    assert r2.status_code == 200
    assert r2.json()["kind"] == "analyze"


def test_task_list() -> None:
    r = client.get("/api/tasks/?limit=5")
    assert r.status_code == 200
    assert "tasks" in r.json()


def test_task_rejected_no_input() -> None:
    r = client.post("/api/tasks/submit", json={"kind": "analyze"})
    assert r.status_code == 400


def test_task_404() -> None:
    assert client.get("/api/tasks/nonexistent").status_code == 404


def test_capture_page() -> None:
    r = client.get("/capture/test-sid?token=abc")
    assert r.status_code == 200
    assert "test-sid" in r.text
    assert "拍摄" in r.text
    assert r.headers["content-type"].startswith("text/html")
    escaped = client.get("/capture/test-sid", params={"token": "';alert(1);//"})
    assert "';alert(1);//" not in escaped.text
    assert "%27%3Balert%281%29%3B%2F%2F" in escaped.text


# ── Worker tests ─────────────────────────────────────────────────

def test_worker_bjt_dc() -> None:
    from services.api.worker import worker

    worker.start()
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "analyze", "benchmark_id": "bjt-common-emitter-bias.json"},
    )
    tid = r.json()["id"]

    for _ in range(20):
        time.sleep(0.5)
        s = client.get(f"/api/tasks/{tid}").json()
        if s["status"] in ("completed", "failed"):
            break

    final = client.get(f"/api/tasks/{tid}").json()
    assert final["status"] == "completed", f"Failed: {final.get('error')}"
    assert final["result"]["problemType"] == "bjt_dc_bias"
    assert len(final["result"]["steps"]) >= 5


def test_worker_validate() -> None:
    from services.api.worker import worker

    worker.start()
    bm_path = _PROJECT_ROOT / "benchmarks" / "examples" / "diode-limiter.json"
    with open(bm_path) as f:
        bm = json.load(f)
    r = client.post(
        "/api/tasks/submit",
        json={"kind": "validate", "circuit_graph": bm["circuitGraph"]},
    )
    tid = r.json()["id"]

    for _ in range(10):
        time.sleep(0.5)
        s = client.get(f"/api/tasks/{tid}").json()
        if s["status"] in ("completed", "failed"):
            break

    final = client.get(f"/api/tasks/{tid}").json()
    assert final["status"] == "completed"
    assert final["result"]["valid"] is True


# ── Runner ───────────────────────────────────────────────────────

if __name__ == "__main__":
    tests = [
        # Sync endpoints
        ("health check", test_health),
        ("create + get session", test_create_and_get_session),
        ("session 404", test_session_404),
        ("image upload", test_image_upload),
        ("upload rejected — no token", test_upload_rejected_no_token),
        ("upload rejected — wrong token", test_upload_rejected_wrong_token),
        ("submit task (benchmark)", test_submit_task_by_benchmark),
        ("submit task (inline)", test_submit_task_inline),
        ("submit task (validate)", test_submit_task_validate),
        ("get task", test_get_task),
        ("task list", test_task_list),
        ("task rejected — no input", test_task_rejected_no_input),
        ("task 404", test_task_404),
        ("capture page HTML", test_capture_page),
        # Worker
        ("worker BJT DC", test_worker_bjt_dc),
        ("worker validate", test_worker_validate),
    ]

    failed = 0
    for name, fn in tests:
        try:
            fn()
            print(f"  OK {name}")
        except Exception as exc:
            failed += 1
            print(f"  FAIL {name}: {exc}")
            import traceback
            traceback.print_exc()

    print(f"\n{'='*50}")
    if failed:
        print(f"FAILED: {failed}/{len(tests)} tests failed.")
        sys.exit(1)
    else:
        print(f"OK: {len(tests)} tests passed.")
