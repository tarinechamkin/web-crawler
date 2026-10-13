using HtmlAgilityPack;

namespace Crawler.Domain.Urls;

public static class HtmlLinkExtractor
{
    /// <summary>
    /// Returns the distinct, normalized http(s) links found in &lt;a href&gt; tags,
    /// in document order. Honors &lt;base href&gt; when resolving relative links.
    /// </summary>
    public static IReadOnlyList<Uri> ExtractLinks(string html, Uri pageUri)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var baseUri = ResolveBaseUri(document, pageUri);
        var anchors = document.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<Uri>();
        foreach (var anchor in anchors)
        {
            var href = HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", ""));
            if (UrlNormalizer.TryNormalize(href, baseUri, out var link) && seen.Add(link.AbsoluteUri))
                links.Add(link);
        }
        return links;
    }

    private static Uri ResolveBaseUri(HtmlDocument document, Uri pageUri)
    {
        var baseHref = document.DocumentNode.SelectSingleNode("//base[@href]")?.GetAttributeValue("href", "");
        return !string.IsNullOrWhiteSpace(baseHref) && Uri.TryCreate(pageUri, baseHref.Trim(), out var baseUri)
            ? baseUri
            : pageUri;
    }
}
