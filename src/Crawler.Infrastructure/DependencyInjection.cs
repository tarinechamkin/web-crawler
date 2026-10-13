using System.Net;
using Crawler.Domain.Crawling;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

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

    /// <summary>
    /// HttpClient for crawling, wrapped in a resilience pipeline (outermost first):
    /// total timeout 30s → retry up to 3 times with exponential backoff + jitter → 10s per attempt.
    /// The retry only fires on transient failures: network errors, timeouts, HTTP 408, 429 and 5xx
    /// (and honors Retry-After). A 404 or 403 is permanent and is not retried.
    /// </summary>
    public static IServiceCollection AddCrawlerHttp(this IServiceCollection services)
    {
        services
            .AddHttpClient<IPageFetcher, HttpPageFetcher>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CrawlerAssessmentBot/1.0");
                client.Timeout = Timeout.InfiniteTimeSpan; // the pipeline below owns timeouts
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2), // pick up DNS changes
            })
            .AddResilienceHandler("page-fetch", pipeline =>
            {
                pipeline.AddTimeout(TimeSpan.FromSeconds(30));
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));
            });
        return services;
    }
}
