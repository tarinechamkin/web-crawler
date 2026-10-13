using Crawler.Domain.Jobs;
using Crawler.Domain.Urls;
using Crawler.Infrastructure.Messaging;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crawler.Api.Jobs;

public abstract record CreateJobResult
{
    public sealed record Created(Guid JobId) : CreateJobResult;
    public sealed record Invalid(IDictionary<string, string[]> Errors) : CreateJobResult;
    public sealed record QueueUnavailable(Guid JobId) : CreateJobResult;
}

public enum CancelJobResult { Canceled, NotFound, AlreadyFinished }

/// <summary>Write side of the API: creating and canceling jobs.</summary>
public sealed class JobService(
    CrawlerDbContext db,
    ICrawlJobPublisher publisher,
    IOptions<CrawlOptions> options,
    TimeProvider clock,
    ILogger<JobService> logger)
{
    public async Task<CreateJobResult> CreateAsync(CreateJobRequest request, CancellationToken ct)
    {
        var errors = Validate(request, out var url, out var maxDepth);
        if (errors.Count > 0)
            return new CreateJobResult.Invalid(errors);

        var job = new CrawlJob
        {
            Id = Guid.NewGuid(),
            Url = url.AbsoluteUri,
            MaxDepth = maxDepth,
            MaxPages = options.Value.MaxPagesPerJob,
            Status = JobStatus.Pending,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        using var _ = logger.BeginScope(new Dictionary<string, object> { ["JobId"] = job.Id });
        try
        {
            // Not tied to the request's token: once the row exists we want the message out.
            await publisher.PublishJobRequestedAsync(
                new CrawlJobRequested(CrawlJobRequested.CurrentSchemaVersion, job.Id, job.Url, job.MaxDepth, job.CreatedAt),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Row saved but message not: without a transactional outbox the job would sit in Pending
            // forever, so fail it visibly instead (see README, "Known limitations").
            logger.LogError(ex, "Could not publish crawl job; marking it Failed");
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.FailureReason, "Could not enqueue the job (message broker unavailable)")
                .SetProperty(j => j.CompletedAt, clock.GetUtcNow()), CancellationToken.None);
            return new CreateJobResult.QueueUnavailable(job.Id);
        }

        logger.LogInformation("Crawl job created for {Url} (maxDepth {MaxDepth})", job.Url, job.MaxDepth);
        return new CreateJobResult.Created(job.Id);
    }

    /// <summary>The worker notices the Canceled status after its current page and stops.</summary>
    public async Task<CancelJobResult> CancelAsync(Guid jobId, CancellationToken ct)
    {
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Canceled)
                .SetProperty(j => j.CompletedAt, clock.GetUtcNow()), ct);
        if (updated > 0)
        {
            logger.LogInformation("Job {JobId} canceled", jobId);
            return CancelJobResult.Canceled;
        }
        return await db.Jobs.AnyAsync(j => j.Id == jobId, ct) ? CancelJobResult.AlreadyFinished : CancelJobResult.NotFound;
    }

    private Dictionary<string, string[]> Validate(CreateJobRequest request, out Uri url, out int maxDepth)
    {
        var errors = new Dictionary<string, string[]>();
        if (!UrlNormalizer.TryNormalizeAbsolute(request.Url, out url))
            errors["url"] = ["Enter an absolute http:// or https:// URL."];

        var limit = options.Value.MaxDepthLimit;
        maxDepth = request.MaxDepth ?? options.Value.DefaultMaxDepth;
        if (maxDepth < 0 || maxDepth > limit)
            errors["maxDepth"] = [$"maxDepth must be between 0 and {limit}."];
        return errors;
    }
}
