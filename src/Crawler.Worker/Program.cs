using Crawler.Domain.Crawling;
using Crawler.Infrastructure;
using Crawler.Infrastructure.Observability;
using Crawler.Worker.Messaging;

// A web host (not a bare worker) only so the process can expose /health.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCrawlerLogging("crawler-worker");
builder.Services
    .AddCrawlerPersistence(builder.Configuration)
    .AddCrawlerHttp()
    .AddCrawlerMessaging()
    .AddCrawlerHealthChecks();
builder.Services.AddScoped<CrawlEngine>();
builder.Services.AddHostedService<CrawlJobConsumer>();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
