using Crawler.Domain.Jobs;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crawler.Api.Jobs;

/// <summary>Read side of the API. All queries are no-tracking and hit an index.</summary>
public sealed class JobQueries(CrawlerDbContext db)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<JobSummaryDto?> GetJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job is null ? null : JobSummaryDto.From(job);
    }

    /// <summary>Works while running too (partial tree). At most MaxPages rows, via the (job_id, url) index.</summary>
    public async Task<JobTreeDto?> GetTreeAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
            return null;

        var pages = await db.Pages.AsNoTracking().Where(p => p.JobId == jobId).ToListAsync(ct);
        var root = PageTree.Build(pages);
        return new JobTreeDto(job.Id, job.Status, pages.Count, root is null ? null : PageNodeDto.From(root));
    }

    /// <summary>Outgoing links of one page, loaded on demand so the tree payload stays small.</summary>
    public async Task<PageLinksDto?> GetPageLinksAsync(Guid jobId, long pageId, CancellationToken ct)
    {
        var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId && p.JobId == jobId, ct);
        if (page is null)
            return null;

        var links = await db.Links.AsNoTracking()
            .Where(l => l.PageId == pageId)
            .OrderBy(l => l.TargetUrl)
            .Select(l => new LinkDto(l.TargetUrl, l.IsInternal))
            .ToListAsync(ct);
        return new PageLinksDto(page.Id, page.Url, links);
    }

    public async Task<JobHistoryDto> GetHistoryAsync(HistoryCursor? cursor, int limit, CancellationToken ct)
    {
        var query = db.Jobs.AsNoTracking();
        if (cursor is { } after)
        {
            // Row-value comparison "(created_at, id) < (@createdAt, @id)" — matches the composite index.
            query = query.Where(j => EF.Functions.LessThan(
                ValueTuple.Create(j.CreatedAt, j.Id), ValueTuple.Create(after.CreatedAt, after.Id)));
        }

        var rows = await query
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Take(limit + 1) // one extra row tells us whether another page exists
            .ToListAsync(ct);

        var items = rows.Take(limit).Select(JobSummaryDto.From).ToList();
        var nextCursor = rows.Count > limit ? new HistoryCursor(items[^1].CreatedAt, items[^1].JobId).Encode() : null;
        return new JobHistoryDto(items, nextCursor);
    }
}
