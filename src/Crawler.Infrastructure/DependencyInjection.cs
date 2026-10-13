using Crawler.Domain.Crawling;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Crawler.Infrastructure;

/// <summary>One place where both services register the infrastructure they share.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCrawlerPersistence(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Crawler")
            ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Crawler'.");

        services.AddDbContext<CrawlerDbContext>(options => ConfigureDb(options, connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICrawlJobStore, CrawlJobStore>();
        return services;
    }

    public static void ConfigureDb(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .UseSnakeCaseNamingConvention(); // CreatedAt → created_at, matching the hand-written SQL
}
