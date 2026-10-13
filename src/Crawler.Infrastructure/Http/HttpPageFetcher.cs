using System.Text;
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

    /// <summary>The statuses SocketsHttpHandler auto-follows (304 etc. are not redirects to follow).</summary>
    private static readonly HashSet<int> RedirectStatuses = [301, 302, 303, 307, 308];

    // Makes legacy charsets such as windows-1252 and iso-8859-2 available to Encoding.GetEncoding.
    static HttpPageFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var finalUri = response.RequestMessage?.RequestUri ?? url; // after redirects
            var status = (int)response.StatusCode;

            if (RedirectStatuses.Contains(status))
                return new FetchResult(finalUri, status, null,
                    DescribeUnfollowedRedirect(finalUri, status, response.Headers.Location));

            if (!response.IsSuccessStatusCode)
                return new FetchResult(finalUri, status, null, $"HTTP {status}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !HtmlMediaTypes.Contains(mediaType))
                return new FetchResult(finalUri, status, null, $"Not HTML ({mediaType ?? "no content type"})");

            if (response.Content.Headers.ContentLength > MaxHtmlBytes)
                return new FetchResult(finalUri, status, null, "Page too large");

            await response.Content.LoadIntoBufferAsync(MaxHtmlBytes); // throws if a chunked body runs past the cap
            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var html = DecodeBody(body, response.Content.Headers.ContentType?.CharSet);
            return new FetchResult(finalUri, status, html, null);
        }
        catch (Exception ex) when (IsFetchFailure(ex, ct))
        {
            logger.LogInformation("Fetching {Url} failed after retries: {Error}", url, ex.Message);
            return new FetchResult(url, null, null, Describe(ex));
        }
    }

    /// <summary>
    /// Decodes with the declared charset when .NET knows it, otherwise UTF-8 (what browsers fall back to).
    /// A bogus charset (crawler-test.com serves some on purpose) must not fail the page.
    /// </summary>
    public static string DecodeBody(byte[] body, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim(' ', '"', '\''));
            }
            catch (ArgumentException)
            {
                // Unknown charset: keep UTF-8.
            }
        }
        return encoding.GetString(body);
    }

    /// <summary>
    /// The handler follows these redirects automatically, so one that reaches us was declined:
    /// an HTTPS → HTTP downgrade (refused for security; seen on quotes.toscrape.com) or too many hops.
    /// </summary>
    public static string DescribeUnfollowedRedirect(Uri requested, int status, Uri? location)
    {
        if (location is null)
            return $"HTTP {status} redirect without a Location header";

        // On Linux a relative header like "/next" parses as the absolute URI file:///next,
        // so only trust it as absolute when it is http(s); otherwise resolve the original text.
        var target = location.IsAbsoluteUri && location.Scheme is "http" or "https"
            ? location
            : new Uri(requested, location.OriginalString);
        return requested.Scheme == Uri.UriSchemeHttps && target.Scheme == Uri.UriSchemeHttp
            ? $"Redirect not followed: HTTPS to HTTP downgrade ({target.AbsoluteUri})"
            : $"Redirect not followed (HTTP {status}, too many redirects?) to {target.AbsoluteUri}";
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
