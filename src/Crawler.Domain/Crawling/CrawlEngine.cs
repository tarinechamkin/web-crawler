using Crawler.Domain.Jobs;
using Crawler.Domain.Urls;
using Microsoft.Extensions.Logging;

namespace Crawler.Domain.Crawling;

/// <summary>
/// Breadth-first crawl of one job. Safe to run more than once for the same job (at-least-once
/// delivery): pages already stored are skipped by the store's unique keys, and terminal jobs are ignored.
/// </summary>
public sealed class CrawlEngine(IPageFetcher fetcher, ICrawlJobStore store, ILogger<CrawlEngine> logger)
{
    // Links with these extensions are recorded but never fetched — they are almost never HTML.
    private static readonly HashSet<string> NonHtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp", ".ico", ".css", ".js",
        ".zip", ".gz", ".mp3", ".mp4", ".avi", ".mov", ".woff", ".woff2", ".xml", ".json",
    };

    private sealed record QueuedPage(Uri Url, int Depth, long? ParentPageId);

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await store.GetJobAsync(jobId, ct) ?? throw new JobNotFoundException(jobId);
        if (job.Status.IsTerminal())
        {
            logger.LogInformation("Job already {Status}; skipping duplicate delivery", job.Status);
            return;
        }
        if (!await store.TryMarkRunningAsync(jobId, ct))
        {
            logger.LogInformation("Job could not be started (finished or canceled meanwhile)");
            return;
        }

        logger.LogInformation("Crawl started for {Url} (maxDepth {MaxDepth}, maxPages {MaxPages})",
            job.Url, job.MaxDepth, job.MaxPages);

        var outcome = await CrawlAsync(job, ct);
        switch (outcome)
        {
            case CrawlOutcome.RootFailed root:
                await store.MarkFailedAsync(jobId, root.Reason, ct);
                logger.LogWarning("Crawl failed: {Reason}", root.Reason);
                break;
            case CrawlOutcome.Canceled:
                logger.LogInformation("Crawl stopped because the job was canceled");
                break;
            case CrawlOutcome.Finished finished:
                await store.MarkCompletedAsync(jobId, ct);
                logger.LogInformation("Crawl completed: {PageCount} pages", finished.PageCount);
                break;
        }
    }

    private abstract record CrawlOutcome
    {
        public sealed record Finished(int PageCount) : CrawlOutcome;
        public sealed record Canceled : CrawlOutcome;
        public sealed record RootFailed(string Reason) : CrawlOutcome;
    }

    private async Task<CrawlOutcome> CrawlAsync(CrawlJob job, CancellationToken ct)
    {
        var startUrl = new Uri(job.Url);
        var startHost = startUrl.Host;
        var visited = new HashSet<string>(StringComparer.Ordinal) { startUrl.AbsoluteUri };
        var queue = new Queue<QueuedPage>();
        queue.Enqueue(new QueuedPage(startUrl, 0, null));
        var pageCount = 0;

        while (queue.Count > 0 && pageCount < job.MaxPages)
        {
            var current = queue.Dequeue();
            var page = await CrawlPageAsync(job.Id, current, startHost, ct);
            var pageId = await store.SavePageAsync(page, ct);
            pageCount++;

            if (current.Depth == 0 && page.Error is not null)
                return new CrawlOutcome.RootFailed($"Start URL could not be crawled: {page.Error}");

            if (await store.UpdateProgressAsync(job.Id, ct) == JobStatus.Canceled)
                return new CrawlOutcome.Canceled();

            if (current.Depth >= job.MaxDepth)
                continue;

            foreach (var link in page.Links)
            {
                if (ShouldFollow(link, startHost) && visited.Add(link.AbsoluteUri))
                    queue.Enqueue(new QueuedPage(link, current.Depth + 1, pageId));
            }
        }

        return new CrawlOutcome.Finished(pageCount);
    }

    /// <summary>
    /// Bulkhead: whatever goes wrong while fetching or parsing one page becomes that page's error.
    /// Only failures outside this method (e.g. the database) fail the message and retry the job.
    /// </summary>
    private async Task<PageResult> CrawlPageAsync(Guid jobId, QueuedPage current, string startHost, CancellationToken ct)
    {
        try
        {
            return await FetchAndAnalyzeAsync(jobId, current, startHost, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Unexpected error crawling {Url}; recording it on the page", current.Url);
            return new PageResult(jobId, startHost, current.Url, current.Depth, current.ParentPageId,
                null, $"Unexpected error: {ex.Message}", [], 0, 0);
        }
    }

    private async Task<PageResult> FetchAndAnalyzeAsync(Guid jobId, QueuedPage current, string startHost, CancellationToken ct)
    {
        var fetch = await fetcher.FetchAsync(current.Url, ct);
        if (!fetch.IsHtml)
        {
            logger.LogDebug("Skipping {Url}: {Error}", current.Url, fetch.Error);
            return new PageResult(jobId, startHost, current.Url, current.Depth, current.ParentPageId,
                fetch.StatusCode, fetch.Error ?? "Not an HTML page", [], 0, 0);
        }

        var links = HtmlLinkExtractor.ExtractLinks(fetch.Html!, fetch.FinalUri);
        var internalCount = links.Count(link => DomainLinkRatio.IsInternal(link, startHost));
        return new PageResult(jobId, startHost, current.Url, current.Depth, current.ParentPageId,
            fetch.StatusCode, null, links, internalCount, DomainLinkRatio.Calculate(links, startHost));
    }

    private static bool ShouldFollow(Uri link, string startHost) =>
        DomainLinkRatio.IsInternal(link, startHost) &&
        !NonHtmlExtensions.Contains(Path.GetExtension(link.AbsolutePath));
}
