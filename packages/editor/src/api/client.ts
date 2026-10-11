/**
 * API client for the local circuit analysis service (FastAPI on :3000).
 *
 * Vite proxies /api → localhost:3000 in dev mode.
 */

import type { CircuitGraph } from "@circuit/schema";
import type { AnalysisResult } from "../types/editor";

export interface TaskSubmitResponse {
  id: string;
  kind: string;
  status: string;
  created_at: string;
}

export interface TaskStatusResponse {
  id: string;
  status: "pending" | "running" | "completed" | "failed";
  kind: string;
  created_at: string;
  started_at?: string;
  finished_at?: string;
  result?: AnalysisResult;
  error?: string;
}

export interface SessionCreateResponse {
  id: string;
  token: string;
  status: string;
  qr_url: string;
  expires_in_seconds: number;
}

export interface UploadedImage {
  id: string;
  filename: string;
  path: string;
  uploaded_at: string;
  url?: string;
}

export interface SessionStatusResponse {
  id: string;
  status: string;
  image_count: number;
  images: UploadedImage[];
  tasks: Array<Record<string, unknown>>;
}

const BASE = "/api";

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${url}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error((err as { detail?: string }).detail ?? `HTTP ${res.status}`);
  }
  return res.json();
}

/** Submit a circuit for analysis. Returns the task ID. */
export function submitAnalysis(
  circuitGraph: CircuitGraph,
): Promise<TaskSubmitResponse> {
  return request<TaskSubmitResponse>("/tasks/submit", {
    method: "POST",
    body: JSON.stringify({ kind: "analyze", circuit_graph: circuitGraph }),
  });
}

/** Poll for task status. */
export function getTaskStatus(
  taskId: string,
): Promise<TaskStatusResponse> {
  return request<TaskStatusResponse>(`/tasks/${taskId}`);
}

/** Create a new mobile upload session with QR code URL and token. */
export function createSession(): Promise<SessionCreateResponse> {
  return request<SessionCreateResponse>("/session/create", {
    method: "POST",
  });
}

/** Get status and uploaded images for a session. */
export function getSession(sessionId: string): Promise<SessionStatusResponse> {
  return request<SessionStatusResponse>(`/session/${sessionId}`);
}

/** Subscribe to session events via SSE. */
export function subscribeSessionEvents(
  sessionId: string,
  handlers: {
    onStatus?: (status: string) => void;
    onImageUploaded?: (image: UploadedImage) => void;
    onError?: (error: Error) => void;
    onComplete?: () => void;
  },
): () => void {
  const eventSource = new EventSource(`${BASE}/session/${sessionId}/events`);

  eventSource.addEventListener("status", (e: MessageEvent) => {
    try {
      const data = JSON.parse(e.data);
      handlers.onStatus?.(data.status);
    } catch {
      // ignore
    }
  });

  eventSource.addEventListener("status_change", (e: MessageEvent) => {
    try {
      const data = JSON.parse(e.data);
      handlers.onStatus?.(data.status);
      if (data.status === "completed") {
        handlers.onComplete?.();
      }
    } catch {
      // ignore
    }
  });

  eventSource.addEventListener("upload_complete", (e: MessageEvent) => {
    try {
      const data = JSON.parse(e.data);
      handlers.onImageUploaded?.(data);
    } catch {
      // ignore
    }
  });

  eventSource.onerror = (e) => {
    handlers.onError?.(new Error("SSE connection error"));
  };

  return () => {
    eventSource.close();
  };
}
