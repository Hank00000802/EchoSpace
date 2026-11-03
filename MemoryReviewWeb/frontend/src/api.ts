import type { SessionView, TrashItemView } from "./types";

export class ApiError extends Error {
  constructor(
    public readonly code: string,
    public readonly status: number,
  ) {
    super(code);
  }
}

export async function fetchSession(sessionId: string, signal?: AbortSignal): Promise<SessionView> {
  let response: Response;
  try {
    response = await fetch("/api/sessions/" + encodeURIComponent(sessionId), {
      cache: "no-store",
      credentials: "omit",
      signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError("network", 0);
  }

  if (!response.ok) {
    let code = "http_" + response.status;
    try {
      const body: unknown = await response.json();
      const candidate = (body as { error?: { code?: unknown } }).error?.code;
      if (typeof candidate === "string") code = candidate;
    } catch {
      // keep the generic code
    }
    throw new ApiError(code, response.status);
  }
  return (await response.json()) as SessionView;
}

async function postTrash(
  sessionId: string,
  path: "trash" | "trash/restore",
  kind: TrashItemView["kind"],
  cardId: string,
): Promise<SessionView> {
  let response: Response;
  try {
    response = await fetch("/api/sessions/" + encodeURIComponent(sessionId) + "/" + path, {
      method: "POST",
      cache: "no-store",
      credentials: "omit",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ kind, cardId }),
    });
  } catch {
    throw new ApiError("network", 0);
  }
  if (!response.ok) {
    let code = "http_" + response.status;
    try {
      const body: unknown = await response.json();
      const candidate = (body as { error?: { code?: unknown } }).error?.code;
      if (typeof candidate === "string") code = candidate;
    } catch {
      // keep the generic code
    }
    throw new ApiError(code, response.status);
  }
  return (await response.json()) as SessionView;
}

export function moveToTrash(sessionId: string, kind: TrashItemView["kind"], cardId: string): Promise<SessionView> {
  return postTrash(sessionId, "trash", kind, cardId);
}

export function restoreFromTrash(sessionId: string, kind: TrashItemView["kind"], cardId: string): Promise<SessionView> {
  return postTrash(sessionId, "trash/restore", kind, cardId);
}

/** Only same-origin API screenshot URLs produced by the backend are ever used as <img src>. */
export function safeScreenshotUrl(url: string): string | null {
  return /^\/api\/sessions\/[A-Za-z0-9_-]+\/cards\/[A-Za-z0-9_-]+\/screenshot$/.test(url) ? url : null;
}
