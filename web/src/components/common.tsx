import type { JobStatus } from '../api/types';

export function StatusBadge({ status }: { status: JobStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <p className="muted" role="status">{label}</p>;
}

export function ErrorMessage({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return <p className="error" role="alert">{message}</p>;
}

export function formatDate(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : '—';
}

export function formatDuration(from: string | null, to: string | null): string {
  if (!from || !to) return '—';
  const seconds = Math.round((new Date(to).getTime() - new Date(from).getTime()) / 1000);
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

export function formatRatio(ratio: number): string {
  return `${Math.round(ratio * 100)}%`;
}
