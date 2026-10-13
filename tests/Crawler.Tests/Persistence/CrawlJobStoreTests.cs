using Crawler.Domain.Crawling;
using Crawler.Domain.Jobs;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Crawler.Tests.Persistence;

/// <summary>
/// Runs against a real, throwaway PostgreSQL in Docker (Testcontainers) to prove the
/// at-least-once guarantees: replayed writes must not create duplicate rows.
/// </summary>
public sealed class CrawlJobStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Saving_the_same_page_twice_keeps_one_page_and_one_set_of_links()
    {
        var job = await CreateJobAsync();
        var page = new PageResult(job.Id, "example.com", new Uri("https://example.com/"), 0, null, 200, null,
            [new Uri("https://example.com/a"), new Uri("https://other.com/b")], 1, 0.5);

        long firstId, secondId;
        await using (var db = CreateContext())
            firstId = await new CrawlJobStore(db, TimeProvider.System).SavePageAsync(page, default);
        await using (var db = CreateContext()) // a redelivered message gets a fresh context
            secondId = await new CrawlJobStore(db, TimeProvider.System).SavePageAsync(page, default);

        await using var check = CreateContext();
        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await check.Pages.CountAsync(p => p.JobId == job.Id));
        Assert.Equal(2, await check.Links.CountAsync(l => l.JobId == job.Id));
        Assert.True(await check.Links.AnyAsync(l => l.TargetUrl == "https://example.com/a" && l.IsInternal));
        Assert.True(await check.Links.AnyAsync(l => l.TargetUrl == "https://other.com/b" && !l.IsInternal));
    }

    [Fact]
    public async Task Progress_is_recomputed_not_incremented()
    {
        var job = await CreateJobAsync();
        await using var db = CreateContext();
        var store = new CrawlJobStore(db, TimeProvider.System);
        await store.SavePageAsync(new PageResult(job.Id, "example.com", new Uri("https://example.com/"),
            0, null, 200, null, [], 0, 0), default);

        await store.UpdateProgressAsync(job.Id, default);
        await store.UpdateProgressAsync(job.Id, default); // replay

        await using var check = CreateContext();
        Assert.Equal(1, (await check.Jobs.SingleAsync(j => j.Id == job.Id)).PagesCrawled);
    }

    [Fact]
    public async Task Completing_does_not_overwrite_a_canceled_job()
    {
        var job = await CreateJobAsync(JobStatus.Canceled);
        await using var db = CreateContext();
        var store = new CrawlJobStore(db, TimeProvider.System);

        Assert.False(await store.TryMarkRunningAsync(job.Id, default));
        await store.MarkCompletedAsync(job.Id, default);

        await using var check = CreateContext();
        Assert.Equal(JobStatus.Canceled, (await check.Jobs.SingleAsync(j => j.Id == job.Id)).Status);
    }

    private async Task<CrawlJob> CreateJobAsync(JobStatus status = JobStatus.Pending)
    {
        var job = new CrawlJob
        {
            Id = Guid.NewGuid(), Url = "https://example.com/", MaxDepth = 2, MaxPages = 200,
            Status = status, CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var db = CreateContext();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private CrawlerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CrawlerDbContext>();
        Infrastructure.DependencyInjection.ConfigureDb(options, _postgres.GetConnectionString());
        return new CrawlerDbContext(options.Options);
    }
}
