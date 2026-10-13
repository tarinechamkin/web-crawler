import { useInfiniteQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { isActive } from '../api/types';
import { ErrorMessage, formatDate, Loading, StatusBadge } from '../components/common';

export function HistoryPage() {
  const navigate = useNavigate();

  // "Load more" pagination: each page's nextCursor is passed to the next request.
  const history = useInfiniteQuery({
    queryKey: ['history'],
    queryFn: ({ pageParam }) => api.getHistory(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    // Refresh only while something on screen can still change.
    refetchInterval: (query) =>
      query.state.data?.pages.some((page) => page.items.some((job) => isActive(job.status))) ? 5000 : false,
  });

  if (history.isPending) return <Loading label="Loading history…" />;
  if (history.isError) return <ErrorMessage error={history.error} />;

  const jobs = history.data.pages.flatMap((page) => page.items);
  return (
    <section>
      <h1>History</h1>
      {jobs.length === 0 ? (
        <p className="muted">No crawls yet. <Link to="/">Start one</Link>.</p>
      ) : (
        <div className="card table-wrap">
          <table>
            <thead>
              <tr><th>Status</th><th>URL</th><th>Pages</th><th>Created</th><th>Completed</th></tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.jobId} className="clickable" onClick={() => navigate(`/jobs/${job.jobId}`)}>
                  <td><StatusBadge status={job.status} /></td>
                  <td>
                    {/* stopPropagation: the row's own onClick must not navigate a second time
                        (and must not hijack Ctrl/middle-click "open in new tab"). */}
                    <Link to={`/jobs/${job.jobId}`} className="url" onClick={(e) => e.stopPropagation()}>
                      {job.url}
                    </Link>
                  </td>
                  <td>{job.pagesCrawled}</td>
                  <td>{formatDate(job.createdAt)}</td>
                  <td>{formatDate(job.completedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {history.hasNextPage && (
        <button className="secondary" onClick={() => history.fetchNextPage()} disabled={history.isFetchingNextPage}>
          {history.isFetchingNextPage ? 'Loading…' : 'Load more'}
        </button>
      )}
    </section>
  );
}
