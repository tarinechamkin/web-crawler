namespace Crawler.Domain.Urls;

public static class DomainLinkRatio
{
    /// <summary>
    /// (# links whose host equals the starting host) / (total # links). Zero links → 0.
    /// Callers pass already-normalized, de-duplicated http(s) links.
    /// </summary>
    public static double Calculate(IReadOnlyCollection<Uri> links, string startHost)
    {
        if (links.Count == 0)
            return 0;

        var internalCount = links.Count(link => IsInternal(link, startHost));
        return (double)internalCount / links.Count;
    }

    /// <summary>
    /// Exact host match, case-insensitive. Subdomains ("blog.example.com") count as external —
    /// the spec defines the starting domain as the host of the initial URL.
    /// </summary>
    public static bool IsInternal(Uri link, string startHost) =>
        string.Equals(link.Host, startHost, StringComparison.OrdinalIgnoreCase);
}
