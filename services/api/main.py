"""Local API service entry point.

FastAPI application that ties together:
  - Session management (QR-code capture sessions for phone upload)
  - Task queue (submit analysis jobs, poll for results)
  - Image upload (phone → local file storage)
  - SSE event streaming (real-time session updates)

Start:  python -m services.api.main
       (or) uvicorn services.api.main:app --host 0.0.0.0 --port 3000
"""

from __future__ import annotations

import sys
from contextlib import asynccontextmanager
from pathlib import Path

# Ensure the project root is importable
_PROJECT_ROOT = Path(__file__).resolve().parents[2]
if str(_PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(_PROJECT_ROOT))

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles

from .config import config
from .models import HealthResponse
from .routes import sessions, tasks, upload
from .worker import worker


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    worker.start()
    yield
    # Shutdown
    worker.stop()


app = FastAPI(
    title="电路分析助手 API",
    description="Local API for circuit image upload, analysis task queue, and session management.",
    version="0.1.0",
    lifespan=lifespan,
)

# CORS — allow all local origins (phone on LAN, localhost variations)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Routes
app.include_router(sessions.router)
app.include_router(tasks.router)
app.include_router(upload.router)

# Static file serving — uploads directory
uploads_path = Path(config.upload_dir)
if uploads_path.exists():
    app.mount("/uploads", StaticFiles(directory=str(uploads_path)), name="uploads")


# ── Health ──────────────────────────────────────────────────────

@app.get("/api/health", response_model=HealthResponse)
def health() -> HealthResponse:
    import sqlite3
    try:
        row = worker.db._conn.execute("SELECT COUNT(*) FROM tasks").fetchone()
        task_count = row[0] if row else 0
    except Exception:
        task_count = 0
    return HealthResponse(status="ok", task_count=task_count)


# ── Capture page (simple HTML served for phone browsers) ────────

@app.get("/capture/{session_id}")
def capture_page(session_id: str, token: str = ""):
    """Serve a minimal HTML page for phone-based upload."""
    return _CAPTURE_HTML.replace("{{SESSION_ID}}", session_id).replace("{{TOKEN}}", token)


_CAPTURE_HTML = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>拍摄电路图</title>
<style>
  body { font-family: system-ui, sans-serif; max-width: 400px; margin: 0 auto; padding: 20px; }
  h1 { font-size: 1.3em; }
  .upload-btn { display: block; width: 100%; padding: 16px; margin: 12px 0;
    border: 2px dashed #999; border-radius: 12px; text-align: center;
    background: #f5f5f5; cursor: pointer; font-size: 1em; }
  .status { margin-top: 16px; padding: 12px; border-radius: 8px; text-align: center; }
  .status.ok { background: #d4edda; color: #155724; }
  .status.error { background: #f8d7da; color: #721c24; }
  input[type=file] { display: none; }
</style>
</head>
<body>
  <h1>📷 拍摄 / 上传电路图</h1>
  <p>会话: {{SESSION_ID}}</p>

  <label class="upload-btn">
    📸 拍照上传
    <input type="file" accept="image/*" capture="environment" onchange="upload(this.files[0])">
  </label>

  <label class="upload-btn">
    🖼 从相册选择
    <input type="file" accept="image/*" onchange="upload(this.files[0])">
  </label>

  <div id="status"></div>

  <script>
    async function upload(file) {
      if (!file) return;
      const status = document.getElementById('status');
      status.className = 'status';
      status.textContent = '上传中...';

      const form = new FormData();
      form.append('image', file);

      try {
        const res = await fetch('/api/session/{{SESSION_ID}}/upload?token={{TOKEN}}', {
          method: 'POST',
          body: form,
        });
        if (res.ok) {
          status.className = 'status ok';
          status.textContent = '✅ 上传成功！可在电脑端查看。';
        } else {
          const err = await res.json();
          status.className = 'status error';
          status.textContent = '❌ 上传失败: ' + (err.detail || res.statusText);
        }
      } catch (e) {
        status.className = 'status error';
        status.textContent = '❌ 网络错误: ' + e.message;
      }
    }
  </script>
</body>
</html>"""


# ── CLI entry ───────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("services.api.main:app", host=config.host, port=config.port, reload=True)
