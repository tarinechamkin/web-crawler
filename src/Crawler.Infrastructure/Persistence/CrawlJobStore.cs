using Crawler.Domain.Crawling;
using Crawler.Domain.Jobs;
using Crawler.Domain.Urls;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// Worker-side persistence. Every write is idempotent so a redelivered message can replay safely:
/// inserts use ON CONFLICT DO NOTHING against the unique keys, status changes are guarded by the
/// current status, and progress is recomputed from row counts instead of incremented.
/// </summary>
public sealed class CrawlJobStore(CrawlerDbContext db, TimeProvider clock) : ICrawlJobStore
{
    public Task<CrawlJob?> GetJobAsync(Guid jobId, CancellationToken ct) =>
        db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var updated = await db.Jobs
            .Where(j => j.Id == jobId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, j => j.StartedAt ?? now), ct); // keep the first start time on replay
        return updated > 0;
    }

    public Task<long> SavePageAsync(PageResult page, CancellationToken ct)
    {
        // The retrying execution strategy re-runs the whole transaction on a transient DB error;
        // that is safe because both statements are idempotent.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var pageId = await UpsertPageAsync(page, transaction, ct);
            await InsertLinksAsync(pageId, page, transaction, ct);
            await transaction.CommitAsync(ct);
            return pageId;
        });
    }

    public async Task<JobStatus> UpdateProgressAsync(Guid jobId, CancellationToken ct)
    {
        const string sql = """
            UPDATE crawl_jobs
               SET pages_crawled = (SELECT count(*) FROM crawled_pages WHERE job_id = @jobId)
             WHERE id = @jobId
            RETURNING status
            """;
        await using var command = await CreateCommandAsync(sql, transaction: null, ct);
        command.Parameters.AddWithValue("jobId", jobId);
        var status = (string?)await command.ExecuteScalarAsync(ct) ?? throw new JobNotFoundException(jobId);
        return Enum.Parse<JobStatus>(status);
    }

    public Task MarkCompletedAsync(Guid jobId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Running) // never overwrite Canceled
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedAt, now), ct);
    }

    public Task MarkFailedAsync(Guid jobId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var truncated = reason.Length > 2000 ? reason[..2000] : reason;
        return db.Jobs
            .Where(j => j.Id == jobId && !(j.Status == JobStatus.Completed || j.Status == JobStatus.Canceled))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.FailureReason, truncated)
                .SetProperty(j => j.CompletedAt, now), ct);
    }

    /// <summary>Inserts the page if new; otherwise returns the id of the row stored by an earlier delivery.</summary>
    private async Task<long> UpsertPageAsync(PageResult page, IDbContextTransaction transaction, CancellationToken ct)
    {
        const string sql = """
            WITH inserted AS (
                INSERT INTO crawled_pages (job_id, url, depth, parent_page_id, http_status, error,
                                           outgoing_link_count, internal_link_count, domain_link_ratio, crawled_at)
                VALUES (@jobId, @url, @depth, @parentId, @httpStatus, @error,
                        @linkCount, @internalCount, @ratio, @crawledAt)
                ON CONFLICT (job_id, url) DO NOTHING
                RETURNING id
            )
            SELECT id FROM inserted
            UNION ALL
            SELECT id FROM crawled_pages WHERE job_id = @jobId AND url = @url
            LIMIT 1
            """;
        await using var command = await CreateCommandAsync(sql, transaction, ct);
        command.Parameters.AddWithValue("jobId", page.JobId);
        command.Parameters.AddWithValue("url", page.Url.AbsoluteUri);
        command.Parameters.AddWithValue("depth", page.Depth);
        command.Parameters.AddWithValue("parentId", (object?)page.ParentPageId ?? DBNull.Value);
        command.Parameters.AddWithValue("httpStatus", (object?)page.HttpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)Truncate(page.Error) ?? DBNull.Value);
        command.Parameters.AddWithValue("linkCount", page.Links.Count);
        command.Parameters.AddWithValue("internalCount", page.InternalLinkCount);
        command.Parameters.AddWithValue("ratio", page.DomainLinkRatio);
        command.Parameters.AddWithValue("crawledAt", clock.GetUtcNow());
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>All links of a page in one round trip: two parallel arrays expanded by unnest().</summary>
    private async Task InsertLinksAsync(long pageId, PageResult page, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (page.Links.Count == 0)
            return;

        const string sql = """
            INSERT INTO page_links (page_id, job_id, target_url, is_internal)
            SELECT @pageId, @jobId, link.url, link.is_internal
              FROM unnest(@urls, @isInternal) AS link(url, is_internal)
            ON CONFLICT (page_id, target_url) DO NOTHING
            """;
        await using var command = await CreateCommandAsync(sql, transaction, ct);
        command.Parameters.AddWithValue("pageId", pageId);
        command.Parameters.AddWithValue("jobId", page.JobId);
        command.Parameters.AddWithValue("urls", page.Links.Select(l => l.AbsoluteUri).ToArray());
        command.Parameters.AddWithValue("isInternal",
            page.Links.Select(l => DomainLinkRatio.IsInternal(l, page.StartHost)).ToArray());
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Raw SQL on the DbContext's own connection, so it can join the context's transaction.</summary>
    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, IDbContextTransaction? transaction, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct); // no-op if already open (e.g. inside a transaction)
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        return new NpgsqlCommand(sql, connection, (NpgsqlTransaction?)transaction?.GetDbTransaction());
    }

    private static string? Truncate(string? value) => value is { Length: > 2000 } ? value[..2000] : value;
}
