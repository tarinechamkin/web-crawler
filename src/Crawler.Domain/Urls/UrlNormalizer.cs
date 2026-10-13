namespace Crawler.Domain.Urls;

/// <summary>
/// Turns raw href values into canonical absolute URLs so that two spellings of the
/// same page ("/about", "HTTP://Example.com:80/about#team") compare as equal.
/// </summary>
public static class UrlNormalizer
{
    /// <summary>
    /// Resolves <paramref name="href"/> against <paramref name="baseUri"/> and normalizes it.
    /// Returns false for links we never count or follow: empty values, pure in-page anchors ("#x"),
    /// and non-http(s) schemes such as mailto:, tel:, javascript:, data:.
    /// </summary>
    public static bool TryNormalize(string? href, Uri baseUri, out Uri normalized)
    {
        normalized = null!;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var trimmed = href.Trim();
        if (trimmed.StartsWith('#'))
            return false; // jumps within the same page; not an outgoing link

        if (!Uri.TryCreate(baseUri, trimmed, out var absolute))
            return false;

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return false;

        normalized = Canonicalize(absolute);
        return true;
    }

    /// <summary>Normalizes an already-absolute URL (e.g. the job's start URL).</summary>
    public static bool TryNormalizeAbsolute(string? url, out Uri normalized)
    {
        normalized = null!;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var absolute))
            return false;
        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return false;

        normalized = Canonicalize(absolute);
        return true;
    }

    // System.Uri already lowercases scheme + host and resolves "." / ".." segments.
    // HttpRequestUrl = scheme + host + non-default port + path + query: it drops the
    // fragment, the user-info and default ports (":80" / ":443") for us.
    private static Uri Canonicalize(Uri absolute) =>
        new(absolute.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped));
}
