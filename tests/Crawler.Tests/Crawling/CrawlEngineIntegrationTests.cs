using Crawler.Domain.Crawling;
using Crawler.Domain.Jobs;
using Crawler.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crawler.Tests.Crawling;

/// <summary>
/// Runs the real CrawlEngine with the real HttpPageFetcher (including its retry pipeline) against a
/// local fixture site served over HTTP. Only the database is replaced, by an in-memory store.
/// </summary>
public sealed class CrawlEngineIntegrationTests : IAsyncLifetime
{
    private FixtureSite _site = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _site = await FixtureSite.StartAsync();
        _services = new ServiceCollection().AddLogging().AddCrawlerHttp().BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task Crawls_the_fixture_site_with_depth_limit_dedup_and_ratios()
    {
        var store = await RunAsync("/index.html", maxDepth: 2);

        Assert.Equal(JobStatus.Completed, store.Job.Status);
        Assert.Equal(
            new[] { "/about.html", "/blog/index.html", "/blog/post-1.html", "/blog/post-2.html", "/feed", "/index.html", "/missing.html" },
            store.Pages.Select(p => p.Page.Url.AbsolutePath).Order());
        Assert.Equal(7, store.Job.PagesCrawled);

        // Root: 6 distinct http(s) links (fragment duplicate, mailto: and #anchor dropped), 5 on our host.
        var root = store.Page(_site.Url("/index.html")).Page;
        Assert.Equal(6, root.Links.Count);
        Assert.Equal(5.0 / 6, root.DomainLinkRatio, precision: 10);
        Assert.Equal(0, root.Depth);

        var blog = store.Page(_site.Url("/blog/index.html"));
        var post1 = store.Page(_site.Url("/blog/post-1.html")).Page;
        Assert.Equal(2, post1.Depth);
        Assert.Equal(blog.Id, post1.ParentPageId); // tree edge: first discoverer is the parent
        Assert.Equal(0.5, post1.DomainLinkRatio);  // deep.html (internal) + external share link

        var post2 = store.Page(_site.Url("/blog/post-2.html")).Page;
        Assert.Empty(post2.Links);
        Assert.Equal(0, post2.DomainLinkRatio);
    }

    [Fact]
    public async Task Records_broken_and_non_html_pages_without_failing_the_job()
    {
        var store = await RunAsync("/index.html", maxDepth: 1);

        var missing = store.Page(_site.Url("/missing.html")).Page;
        Assert.Equal(404, missing.HttpStatus);
        Assert.Equal("HTTP 404", missing.Error);

        var feed = store.Page(_site.Url("/feed")).Page;
        Assert.StartsWith("Not HTML", feed.Error);
        Assert.Equal(JobStatus.Completed, store.Job.Status);
    }

    [Fact]
    public async Task Never_fetches_pdfs_or_external_links()
    {
        var store = await RunAsync("/index.html", maxDepth: 2);

        Assert.DoesNotContain(store.Pages, p => p.Page.Url.AbsolutePath.EndsWith(".pdf"));
        Assert.All(store.Pages, p => Assert.Equal(_site.BaseUri.Host, p.Page.Url.Host));
    }

    [Theory]
    [InlineData(0, 1)] // depth 0 = only the start page
    [InlineData(1, 5)] // + about, blog, missing, feed
    public async Task Respects_max_depth(int maxDepth, int expectedPages)
    {
        var store = await RunAsync("/index.html", maxDepth);
        Assert.Equal(expectedPages, store.Pages.Count);
    }

    [Fact]
    public async Task Stops_at_the_max_pages_limit()
    {
        var store = await RunAsync("/index.html", maxDepth: 2, maxPages: 3);
        Assert.Equal(3, store.Pages.Count);
        Assert.Equal(JobStatus.Completed, store.Job.Status);
    }

    [Fact]
    public async Task Fails_the_job_when_the_start_url_is_broken()
    {
        var store = await RunAsync("/missing.html", maxDepth: 2);
        Assert.Equal(JobStatus.Failed, store.Job.Status);
        Assert.Contains("HTTP 404", store.Job.FailureReason);
    }

    [Fact]
    public async Task Retries_a_transient_503()
    {
        var store = await RunAsync("/flaky.html", maxDepth: 0);
        Assert.Equal(2, _site.FlakyCalls); // first attempt 503, retry succeeded
        Assert.Equal(JobStatus.Completed, store.Job.Status);
        Assert.Null(store.Page(_site.Url("/flaky.html")).Page.Error);
    }

    [Fact]
    public async Task Stops_when_the_job_is_canceled()
    {
        var store = await RunAsync("/index.html", maxDepth: 2, cancelAfterPages: 2);
        Assert.Equal(JobStatus.Canceled, store.Job.Status);
        Assert.Equal(2, store.Pages.Count);
    }

    [Fact]
    public async Task Redelivered_message_is_safe()
    {
        var store = await RunAsync("/index.html", maxDepth: 2);
        var engine = CreateEngine(store);

        // Duplicate after completion: ignored.
        await engine.RunAsync(store.Job.Id, default);
        Assert.Equal(JobStatus.Completed, store.Job.Status);

        // Crash before ack (job still Running): replays the crawl without duplicating pages.
        store.Job.Status = JobStatus.Running;
        await engine.RunAsync(store.Job.Id, default);
        Assert.Equal(7, store.Pages.Count);
        Assert.Equal(JobStatus.Completed, store.Job.Status);
    }

    [Fact]
    public async Task Page_with_an_unknown_charset_is_still_parsed()
    {
        var store = await RunAsync("/bad-charset.html", maxDepth: 1);

        Assert.Equal(JobStatus.Completed, store.Job.Status);
        Assert.Null(store.Page(_site.Url("/bad-charset.html")).Page.Error);
        Assert.Contains(store.Pages, p => p.Page.Url.AbsolutePath == "/about.html");
    }

    [Fact]
    public async Task An_unexpected_error_on_one_page_does_not_fail_the_job()
    {
        var store = NewStore("/index.html", maxDepth: 1, maxPages: 200, cancelAfterPages: null);
        var fetcher = new ThrowingFetcher(_services.GetRequiredService<IPageFetcher>(), failPath: "/about.html");
        await new CrawlEngine(fetcher, store, NullLogger<CrawlEngine>.Instance).RunAsync(store.Job.Id, default);

        Assert.Equal(JobStatus.Completed, store.Job.Status);
        Assert.StartsWith("Unexpected error", store.Page(_site.Url("/about.html")).Page.Error);
        Assert.Equal(5, store.Pages.Count); // the rest of depth 1 was still crawled
    }

    private sealed class ThrowingFetcher(IPageFetcher inner, string failPath) : IPageFetcher
    {
        public Task<FetchResult> FetchAsync(Uri url, CancellationToken ct) =>
            url.AbsolutePath == failPath ? throw new InvalidOperationException("boom") : inner.FetchAsync(url, ct);
    }

    [Fact]
    public async Task Unknown_job_throws_so_the_message_can_be_dead_lettered()
    {
        var store = NewStore("/index.html", maxDepth: 2, maxPages: 200, cancelAfterPages: null);
        await Assert.ThrowsAsync<JobNotFoundException>(() => CreateEngine(store).RunAsync(Guid.NewGuid(), default));
    }

    private async Task<InMemoryCrawlJobStore> RunAsync(string path, int maxDepth, int maxPages = 200, int? cancelAfterPages = null)
    {
        var store = NewStore(path, maxDepth, maxPages, cancelAfterPages);
        await CreateEngine(store).RunAsync(store.Job.Id, default);
        return store;
    }

    private InMemoryCrawlJobStore NewStore(string path, int maxDepth, int maxPages, int? cancelAfterPages) =>
        new(new CrawlJob
        {
            Id = Guid.NewGuid(), Url = _site.Url(path).AbsoluteUri, MaxDepth = maxDepth, MaxPages = maxPages,
            Status = JobStatus.Pending, CreatedAt = DateTimeOffset.UtcNow,
        })
        { CancelAfterPages = cancelAfterPages };

    private CrawlEngine CreateEngine(InMemoryCrawlJobStore store) =>
        new(_services.GetRequiredService<IPageFetcher>(), store, NullLogger<CrawlEngine>.Instance);
}
