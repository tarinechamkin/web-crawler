using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Crawler.Tests.Crawling;

/// <summary>
/// A real HTTP server on a random localhost port serving Fixtures/site/*.html, plus two special routes:
/// "/feed" (JSON, to test the HTML-only rule) and "/flaky.html" (503 on the first call, to test retries).
/// </summary>
public sealed class FixtureSite : IAsyncDisposable
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "site");
    private readonly WebApplication _app;
    private int _flakyCalls;

    private FixtureSite(WebApplication app) => _app = app;

    public Uri BaseUri => new(_app.Urls.First());
    public int FlakyCalls => _flakyCalls;

    public Uri Url(string path) => new(BaseUri, path);

    public static async Task<FixtureSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // port 0 = any free port
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var site = new FixtureSite(app);

        app.MapGet("/feed", () => Results.Json(new { items = Array.Empty<string>() }));
        app.MapGet("/flaky.html", () => Interlocked.Increment(ref site._flakyCalls) == 1
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Results.Content("<html><body><a href=\"/about.html\">About</a></body></html>", "text/html"));
        app.MapGet("/bad-charset.html", () => Results.Text(
            "<html><body><a href=\"/about.html\">About</a></body></html>", "text/html; charset=no-such-charset"));
        app.MapGet("/{**path}", (string? path) =>
        {
            var file = Path.Combine(Root, path ?? "index.html");
            return File.Exists(file)
                ? Results.Content(File.ReadAllText(file), "text/html; charset=utf-8")
                : Results.NotFound();
        });

        await app.StartAsync();
        return site;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
