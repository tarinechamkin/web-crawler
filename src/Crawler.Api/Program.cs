using System.Text.Json.Serialization;
using Crawler.Api.Jobs;
using Crawler.Infrastructure;
using Crawler.Infrastructure.Observability;
using Crawler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCrawlerLogging("crawler-api");
builder.Services
    .AddCrawlerPersistence(builder.Configuration)
    .AddCrawlerMessaging()
    .AddCrawlerHealthChecks();
builder.Services.Configure<CrawlOptions>(builder.Configuration.GetSection("Crawl"));
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<JobQueries>();

builder.Services.AddProblemDetails(); // errors as RFC 7807 JSON
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseCors();

app.MapHealthChecks("/health");
app.MapJobEndpoints();

await MigrateDatabaseAsync(app);
app.Run();

// Only the API applies migrations, so two services never race to change the schema.
static async Task MigrateDatabaseAsync(WebApplication app)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CrawlerDbContext>().Database.MigrateAsync();
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}): {Error}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

public partial class Program; // lets tests reference the API assembly
