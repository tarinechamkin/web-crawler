using Crawler.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping of the three tables. Used for migrations and API reads; the worker's hot write
/// path uses hand-written SQL (see <see cref="CrawlJobStore"/>) for upserts EF cannot express.
/// </summary>
public class CrawlerDbContext(DbContextOptions<CrawlerDbContext> options) : DbContext(options)
{
    public DbSet<CrawlJob> Jobs => Set<CrawlJob>();
    public DbSet<CrawledPage> Pages => Set<CrawledPage>();
    public DbSet<PageLink> Links => Set<PageLink>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CrawlJob>(job =>
        {
            job.ToTable("crawl_jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Url).IsRequired();
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
            job.Property(j => j.FailureReason).HasMaxLength(2000);
            job.Ignore(j => j.StartHost);

            // History screen: newest first, keyset-paginated on (created_at, id).
            job.HasIndex(j => new { j.CreatedAt, j.Id }).IsDescending();
        });

        model.Entity<CrawledPage>(page =>
        {
            page.ToTable("crawled_pages");
            page.HasKey(p => p.Id);
            page.Property(p => p.Url).IsRequired();
            page.Property(p => p.Error).HasMaxLength(2000);

            // Idempotency: one row per normalized URL per job. Also serves "all pages of a job" lookups,
            // because job_id is the leading column.
            page.HasIndex(p => new { p.JobId, p.Url }).IsUnique();

            page.HasOne<CrawlJob>().WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
            page.HasOne<CrawledPage>().WithMany().HasForeignKey(p => p.ParentPageId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<PageLink>(link =>
        {
            link.ToTable("page_links");

            // Idempotency: the same outgoing link is stored once per page.
            link.HasKey(l => new { l.PageId, l.TargetUrl });
            link.HasIndex(l => l.JobId);

            link.HasOne<CrawledPage>().WithMany().HasForeignKey(l => l.PageId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne<CrawlJob>().WithMany().HasForeignKey(l => l.JobId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
