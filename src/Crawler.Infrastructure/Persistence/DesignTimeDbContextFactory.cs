using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crawler.Infrastructure.Persistence;

/// <summary>Lets the `dotnet ef` CLI build the context to generate migrations. Not used at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CrawlerDbContext>
{
    public CrawlerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CrawlerDbContext>();
        DependencyInjection.ConfigureDb(options, "Host=localhost;Database=crawler;Username=crawler;Password=crawler");
        return new CrawlerDbContext(options.Options);
    }
}
