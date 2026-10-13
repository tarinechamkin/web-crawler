using Crawler.Domain.Jobs;

namespace Crawler.Domain.Crawling;

/// <summary>Downloads one URL. Implementations own timeouts and HTTP-level retries.</summary>
public interface IPageFetcher
{
    Task<FetchResult> FetchAsync(Uri url, CancellationToken ct);
}

/// <param name="FinalUri">URL after redirects; relative links are resolved against it.</param>
/// <param name="Html">Body when the response was a successful text/html page, otherwise null.</param>
/// <param name="Error">Why the page could not be crawled (HTTP error, timeout, not HTML...).</param>
public sealed record FetchResult(Uri FinalUri, int? StatusCode, string? Html, string? Error)
{
    public bool IsHtml => Html is not null;
}

/// <summary>Persistence the crawl engine needs. Every write must be idempotent (safe to replay).</summary>
public interface ICrawlJobStore
{
    Task<CrawlJob?> GetJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>Pending/Running → Running. False if the job was finished or canceled meanwhile.</summary>
    Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken ct);

    /// <summary>Inserts the page and its links if absent; returns the page id either way.</summary>
    Task<long> SavePageAsync(PageResult page, CancellationToken ct);

    /// <summary>Refreshes the job's progress counter and returns its current status (detects cancel).</summary>
    Task<JobStatus> UpdateProgressAsync(Guid jobId, CancellationToken ct);

    Task MarkCompletedAsync(Guid jobId, CancellationToken ct);
    Task MarkFailedAsync(Guid jobId, string reason, CancellationToken ct);
}

public sealed record PageResult(
    Guid JobId,
    string StartHost,
    Uri Url,
    int Depth,
    long? ParentPageId,
    int? HttpStatus,
    string? Error,
    IReadOnlyList<Uri> Links,
    int InternalLinkCount,
    double DomainLinkRatio);

/// <summary>A job referenced by a message does not exist — retrying can never succeed.</summary>
public sealed class JobNotFoundException(Guid jobId) : Exception($"Crawl job {jobId} does not exist.");
