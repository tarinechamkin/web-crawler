using Crawler.Infrastructure.Messaging;
using Crawler.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;

namespace Crawler.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// JSON logs to stdout (one object per line) so a log collector can index fields like JobId.
    /// Log scopes (BeginScope with JobId/MessageId) become properties on every line inside them.
    /// Levels are configurable under "Serilog" in appsettings.json.
    /// </summary>
    public static IServiceCollection AddCrawlerLogging(this IServiceCollection services, string serviceName) =>
        services.AddSerilog((provider, logger) => logger
            .ReadFrom.Configuration(provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>())
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

    /// <summary>Readiness = can reach both PostgreSQL and RabbitMQ.</summary>
    public static IServiceCollection AddCrawlerHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<CrawlerDbContext>("postgres")
            .AddCheck<RabbitMqHealthCheck>("rabbitmq");
        return services;
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await connection.GetAsync(timeout.Token);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Connection closed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ unreachable", ex);
        }
    }
}
