namespace Crawler.Domain.Jobs;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
}

public static class JobStatusExtensions
{
    public static bool IsTerminal(this JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Failed or JobStatus.Canceled;
}

/// <summary>Table "crawl_jobs": one row per user request.</summary>
public class CrawlJob
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public int MaxDepth { get; set; }
    public int MaxPages { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public int PagesCrawled { get; set; }

    public string StartHost => new Uri(Url).Host;
}

/// <summary>Table "crawled_pages": one row per (job, normalized URL). Unique on (JobId, Url).</summary>
public class CrawledPage
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public required string Url { get; set; }
    public int Depth { get; set; }

    /// <summary>The page that first discovered this one (tree view). Null for the root.</summary>
    public long? ParentPageId { get; set; }

    public int? HttpStatus { get; set; }
    public string? Error { get; set; }
    public int OutgoingLinkCount { get; set; }
    public int InternalLinkCount { get; set; }
    public double DomainLinkRatio { get; set; }
    public DateTimeOffset CrawledAt { get; set; }
}

/// <summary>
/// Table "page_links": the edges (parent page → outgoing link). TargetUrl is a URL rather than a
/// page id because external links are recorded but never crawled. Unique on (PageId, TargetUrl).
/// </summary>
public class PageLink
{
    public long PageId { get; set; }
    public Guid JobId { get; set; }
    public required string TargetUrl { get; set; }
    public bool IsInternal { get; set; }
}
