using Crawler.Domain.Jobs;

namespace Crawler.Api.Jobs;

// The JSON shapes of the REST API. Kept separate from the database entities so the
// schema and the public contract can change independently.

public sealed record CreateJobRequest(string? Url, int? MaxDepth);

public sealed record CreateJobResponse(Guid JobId);

public sealed record JobSummaryDto(
    Guid JobId,
    string Url,
    JobStatus Status,
    int MaxDepth,
    int MaxPages,
    int PagesCrawled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason)
{
    public static JobSummaryDto From(CrawlJob job) => new(job.Id, job.Url, job.Status, job.MaxDepth, job.MaxPages,
        job.PagesCrawled, job.CreatedAt, job.StartedAt, job.CompletedAt, job.FailureReason);
}

public sealed record JobHistoryDto(IReadOnlyList<JobSummaryDto> Items, string? NextCursor);

public sealed record JobTreeDto(Guid JobId, JobStatus Status, int PageCount, PageNodeDto? Root);

public sealed record PageNodeDto(
    long PageId,
    string Url,
    int Depth,
    int? HttpStatus,
    string? Error,
    int OutgoingLinkCount,
    int InternalLinkCount,
    double DomainLinkRatio,
    IReadOnlyList<PageNodeDto> Children)
{
    public static PageNodeDto From(PageTreeNode node) => new(node.Page.Id, node.Page.Url, node.Page.Depth,
        node.Page.HttpStatus, node.Page.Error, node.Page.OutgoingLinkCount, node.Page.InternalLinkCount,
        node.Page.DomainLinkRatio, node.Children.Select(From).ToList());
}

public sealed record PageLinksDto(long PageId, string Url, IReadOnlyList<LinkDto> Links);

public sealed record LinkDto(string Url, bool IsInternal);
