using System.Net.Http.Headers;
using Crawler.Domain.Crawling;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace Crawler.Infrastructure.Http;

/// <summary>
/// Downloads one page. Timeouts and retries are not in this class: they are a pipeline wrapped
/// around the HttpClient (see <see cref="DependencyInjection.AddCrawlerHttp"/>). By the time an
/// exception reaches us, retries are exhausted, so every failure becomes an error on the page result.
/// </summary>
public sealed class HttpPageFetcher(HttpClient http, ILogger<HttpPageFetcher> logger) : IPageFetcher
{
    /// <summary>Pages bigger than this are skipped, protecting worker memory.</summary>
    public const int MaxHtmlBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> HtmlMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml",
    };

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var finalUri = response.RequestMessage?.RequestUri ?? url; // after redirects
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                return new FetchResult(finalUri, status, null, $"HTTP {status}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !HtmlMediaTypes.Contains(mediaType))
                return new FetchResult(finalUri, status, null, $"Not HTML ({mediaType ?? "no content type"})");

            if (response.Content.Headers.ContentLength > MaxHtmlBytes)
                return new FetchResult(finalUri, status, null, "Page too large");

            await response.Content.LoadIntoBufferAsync(MaxHtmlBytes); // throws if a chunked body runs past the cap
            var html = await response.Content.ReadAsStringAsync(ct);  // honors the charset in Content-Type
            return new FetchResult(finalUri, status, html, null);
        }
        catch (Exception ex) when (IsFetchFailure(ex, ct))
        {
            logger.LogInformation("Fetching {Url} failed after retries: {Error}", url, ex.Message);
            return new FetchResult(url, null, null, Describe(ex));
        }
    }

    // A canceled *worker* (ct fired) must propagate so the message is not acked; everything else
    // is a failure of this one page.
    private static bool IsFetchFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or TimeoutRejectedException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static string Describe(Exception ex) => ex switch
    {
        TimeoutRejectedException or OperationCanceledException => "Timed out",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "Host not found",
        _ => ex.Message,
    };
}
