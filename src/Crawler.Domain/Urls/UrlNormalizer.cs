namespace Crawler.Domain.Urls;

/// <summary>
/// Turns raw href values into canonical absolute URLs so that two spellings of the
/// same page ("/about", "HTTP://Example.com:80/about#team") compare as equal.
/// </summary>
public static class UrlNormalizer
{
    /// <summary>
    /// Longer URLs are rejected: browsers commonly cap around 2k, and PostgreSQL B-tree index
    /// entries (our unique (job_id, url) key) cannot exceed ~2.7 KB.
    /// </summary>
    public const int MaxUrlLength = 2048;

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

        return TryCanonicalize(absolute, out normalized);
    }

    /// <summary>Normalizes an already-absolute URL (e.g. the job's start URL).</summary>
    public static bool TryNormalizeAbsolute(string? url, out Uri normalized)
    {
        normalized = null!;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var absolute))
            return false;
        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return false;

        return TryCanonicalize(absolute, out normalized);
    }

    // System.Uri already lowercases scheme + host and resolves "." / ".." segments.
    // HttpRequestUrl = scheme + host + non-default port + path + query: it drops the
    // fragment, the user-info and default ports (":80" / ":443") for us.
    private static bool TryCanonicalize(Uri absolute, out Uri normalized)
    {
        normalized = new Uri(absolute.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped));
        return normalized.AbsoluteUri.Length <= MaxUrlLength;
    }
}
