import { useState, type FormEvent } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { ErrorMessage } from '../components/common';

export function StartCrawlPage() {
  // useState = a component field that re-renders the view when its setter is called.
  const [url, setUrl] = useState('');
  const [maxDepth, setMaxDepth] = useState('');
  const navigate = useNavigate();

  // useMutation = a "write" request with built-in isPending / error state.
  const createJob = useMutation({
    mutationFn: () => api.createJob(url.trim(), maxDepth === '' ? undefined : Number(maxDepth)),
    onSuccess: ({ jobId }) => navigate(`/jobs/${jobId}`),
  });

  const fieldErrors = createJob.error instanceof ApiError ? createJob.error.fieldErrors : {};
  const hasFieldErrors = Object.keys(fieldErrors).length > 0;

  function submit(event: FormEvent) {
    event.preventDefault(); // stop the browser's full-page form submit
    createJob.mutate();
  }

  return (
    <section>
      <h1>Start a crawl</h1>
      <form className="card form" onSubmit={submit} noValidate>
        <label>
          Website URL
          <input
            type="url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://example.com"
            required
            autoFocus
          />
        </label>
        {fieldErrors.url && <p className="error">{fieldErrors.url.join(' ')}</p>}

        <label>
          Max depth <span className="muted">(optional, 0–5, default 2)</span>
          <input
            type="number"
            min={0}
            max={5}
            value={maxDepth}
            onChange={(e) => setMaxDepth(e.target.value)}
            placeholder="2"
          />
        </label>
        {fieldErrors.maxDepth && <p className="error">{fieldErrors.maxDepth.join(' ')}</p>}

        <button type="submit" disabled={createJob.isPending || url.trim() === ''}>
          {createJob.isPending ? 'Starting…' : 'Start Crawl'}
        </button>
        {createJob.error && !hasFieldErrors && <ErrorMessage error={createJob.error} />}
      </form>
    </section>
  );
}
