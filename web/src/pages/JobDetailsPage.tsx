import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { isActive } from '../api/types';
import { ErrorMessage, formatDate, formatDuration, Loading, StatusBadge } from '../components/common';
import { PageTree } from '../components/PageTree';

const POLL_INTERVAL_MS = 1500;

export function JobDetailsPage() {
  const { jobId = '' } = useParams(); // like ActivatedRoute.snapshot.paramMap.get('jobId')
  const queryClient = useQueryClient();

  // Polls every 1.5s while the job is Pending/Running and stops by itself once it finishes.
  const job = useQuery({
    queryKey: ['job', jobId],
    queryFn: () => api.getJob(jobId),
    refetchInterval: (query) => (isActive(query.state.data?.status) ? POLL_INTERVAL_MS : false),
  });

  const finished = job.data !== undefined && !isActive(job.data.status);
  const tree = useQuery({
    queryKey: ['tree', jobId],
    queryFn: () => api.getTree(jobId),
    enabled: finished, // only fetched once the crawl is over
  });

  const cancel = useMutation({
    mutationFn: () => api.cancelJob(jobId),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['job', jobId] }), // refresh status now
  });

  if (job.isPending) return <Loading label="Loading job…" />;
  if (job.error instanceof ApiError && job.error.status === 404) return <p className="error">Job not found.</p>;
  // Only a failed *first* load replaces the page; a failed background poll keeps showing the last data.
  if (job.data === undefined) return <ErrorMessage error={job.error} />;

  const j = job.data;
  return (
    <section>
      <h1>Job details</h1>
      {job.isError && <p className="warning">Could not refresh — showing the last known status. Retrying…</p>}

      <div className="card">
        <div className="row">
          <StatusBadge status={j.status} />
          <a href={j.url} target="_blank" rel="noreferrer" className="url">{j.url}</a>
          {isActive(j.status) && (
            <button className="secondary" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
              {cancel.isPending ? 'Canceling…' : 'Cancel'}
            </button>
          )}
        </div>
        {cancel.isError && <ErrorMessage error={cancel.error} />}

        <dl className="facts">
          <dt>Job ID</dt><dd className="mono">{j.jobId}</dd>
          <dt>Max depth</dt><dd>{j.maxDepth}</dd>
          <dt>Created</dt><dd>{formatDate(j.createdAt)}</dd>
          <dt>Started</dt><dd>{formatDate(j.startedAt)}</dd>
          <dt>Completed</dt><dd>{formatDate(j.completedAt)}</dd>
          <dt>Duration</dt><dd>{formatDuration(j.startedAt, j.completedAt)}</dd>
        </dl>

        {j.failureReason && <p className="error">Failure reason: {j.failureReason}</p>}

        {/* We cannot know a site's size in advance, so progress is a count, not a fake percentage. */}
        <div className="progress-text">
          {j.pagesCrawled} page{j.pagesCrawled === 1 ? '' : 's'} crawled
          <span className="muted"> (limit {j.maxPages})</span>
        </div>
        {isActive(j.status) && <div className="progress" aria-label="Crawling in progress"><div /></div>}
      </div>

      {finished && (
        <>
          <h2>Discovered pages</h2>
          {tree.isPending && <Loading label="Loading results…" />}
          {tree.isError && <ErrorMessage error={tree.error} />}
          {tree.data && (tree.data.root
            ? <PageTree jobId={jobId} root={tree.data.root} />
            : <p className="muted">No pages were crawled.</p>)}
        </>
      )}
    </section>
  );
}
