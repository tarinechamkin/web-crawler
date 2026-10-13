using Crawler.Domain.Crawling;
using Crawler.Domain.Jobs;

namespace Crawler.Tests.Crawling;

/// <summary>Test double for the database, with the same de-duplication rule as the real unique key.</summary>
public sealed class InMemoryCrawlJobStore(CrawlJob job) : ICrawlJobStore
{
    private readonly Dictionary<string, (long Id, PageResult Page)> _pages = new();

    public CrawlJob Job { get; } = job;

    /// <summary>Simulates a user pressing Cancel once this many pages are stored.</summary>
    public int? CancelAfterPages { get; init; }

    public IReadOnlyCollection<(long Id, PageResult Page)> Pages => _pages.Values;

    public (long Id, PageResult Page) Page(Uri url) => _pages[url.AbsoluteUri];

    public Task<CrawlJob?> GetJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult(jobId == Job.Id ? Job : null);

    public Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken ct)
    {
        if (Job.Status is not (JobStatus.Pending or JobStatus.Running))
            return Task.FromResult(false);
        Job.Status = JobStatus.Running;
        return Task.FromResult(true);
    }

    public Task<long> SavePageAsync(PageResult page, CancellationToken ct)
    {
        var key = page.Url.AbsoluteUri;
        if (!_pages.TryGetValue(key, out var existing))
        {
            existing = (_pages.Count + 1, page);
            _pages[key] = existing;
        }
        return Task.FromResult(existing.Id);
    }

    public Task<JobStatus> UpdateProgressAsync(Guid jobId, CancellationToken ct)
    {
        Job.PagesCrawled = _pages.Count;
        if (_pages.Count >= CancelAfterPages)
            Job.Status = JobStatus.Canceled;
        return Task.FromResult(Job.Status);
    }

    public Task MarkCompletedAsync(Guid jobId, CancellationToken ct)
    {
        if (Job.Status == JobStatus.Running)
            Job.Status = JobStatus.Completed;
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid jobId, string reason, CancellationToken ct)
    {
        Job.Status = JobStatus.Failed;
        Job.FailureReason = reason;
        return Task.CompletedTask;
    }
}
