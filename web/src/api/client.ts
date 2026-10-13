import type { JobHistory, JobSummary, JobTree, PageLinks } from './types';

/** An HTTP error with the API's ProblemDetails message and per-field validation errors. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
  } catch {
    throw new ApiError('Cannot reach the server. Is the API running?', 0);
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(
      problem?.detail ?? problem?.title ?? `Request failed (${response.status})`,
      response.status,
      problem?.errors ?? {},
    );
  }

  const text = await response.text(); // 202 Accepted from cancel has no body
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  createJob: (url: string, maxDepth?: number) =>
    request<{ jobId: string }>('/api/jobs', { method: 'POST', body: JSON.stringify({ url, maxDepth }) }),

  getJob: (jobId: string) => request<JobSummary>(`/api/jobs/${jobId}`),

  getTree: (jobId: string) => request<JobTree>(`/api/jobs/${jobId}/tree`),

  getPageLinks: (jobId: string, pageId: number) => request<PageLinks>(`/api/jobs/${jobId}/pages/${pageId}/links`),

  getHistory: (cursor?: string) =>
    request<JobHistory>(`/api/jobs?limit=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`),

  cancelJob: (jobId: string) => request<void>(`/api/jobs/${jobId}/cancel`, { method: 'POST' }),
};
