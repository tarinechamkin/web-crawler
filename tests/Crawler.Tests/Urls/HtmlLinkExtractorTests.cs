using Crawler.Domain.Urls;

namespace Crawler.Tests.Urls;

public class HtmlLinkExtractorTests
{
    private static readonly Uri Page = new("https://example.com/docs/index.html");

    private static string[] Extract(string html) =>
        HtmlLinkExtractor.ExtractLinks(html, Page).Select(u => u.AbsoluteUri).ToArray();

    [Fact]
    public void Extracts_and_resolves_anchor_links_in_document_order()
    {
        var html = """<a href="/a">A</a> <p><a href="b.html">B</a></p> <a href="https://other.com/">X</a>""";
        Assert.Equal(["https://example.com/a", "https://example.com/docs/b.html", "https://other.com/"], Extract(html));
    }

    [Fact]
    public void Removes_duplicates_after_normalization()
    {
        var html = """<a href="/a">1</a> <a href="/a#top">2</a> <a href="HTTPS://EXAMPLE.COM/a">3</a>""";
        Assert.Equal(["https://example.com/a"], Extract(html));
    }

    [Fact]
    public void Ignores_non_http_links_and_in_page_anchors()
    {
        var html = """<a href="mailto:x@y.z">m</a> <a href="tel:1">t</a> <a href="#s">s</a> <a>no href</a> <a href="/ok">ok</a>""";
        Assert.Equal(["https://example.com/ok"], Extract(html));
    }

    [Fact]
    public void Honors_base_href()
    {
        var html = """<html><head><base href="https://example.com/v2/"></head><body><a href="page">p</a></body></html>""";
        Assert.Equal(["https://example.com/v2/page"], Extract(html));
    }

    [Fact]
    public void Decodes_html_entities_in_href()
    {
        var html = """<a href="/search?a=1&amp;b=2">s</a>""";
        Assert.Equal(["https://example.com/search?a=1&b=2"], Extract(html));
    }

    [Fact]
    public void Page_without_links_returns_empty()
    {
        Assert.Empty(Extract("<html><body><p>No links here</p></body></html>"));
    }
}
