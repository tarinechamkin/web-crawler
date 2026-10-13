using Crawler.Domain.Urls;

namespace Crawler.Tests.Urls;

public class UrlNormalizerTests
{
    private static readonly Uri Page = new("https://example.com/blog/post-1");

    [Theory]
    [InlineData("/about", "https://example.com/about")]                    // root-relative
    [InlineData("post-2", "https://example.com/blog/post-2")]              // relative to current folder
    [InlineData("../contact", "https://example.com/contact")]              // parent folder
    [InlineData("./post-3?page=2", "https://example.com/blog/post-3?page=2")] // query string is kept
    [InlineData("//cdn.other.com/x", "https://cdn.other.com/x")]           // protocol-relative
    [InlineData("https://other.com/a", "https://other.com/a")]             // already absolute
    public void Resolves_relative_links_against_the_page(string href, string expected)
    {
        Assert.True(UrlNormalizer.TryNormalize(href, Page, out var result));
        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("/about#team", "https://example.com/about")]
    [InlineData("https://example.com/blog/post-1#comments", "https://example.com/blog/post-1")]
    public void Drops_the_fragment(string href, string expected)
    {
        Assert.True(UrlNormalizer.TryNormalize(href, Page, out var result));
        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("HTTPS://EXAMPLE.COM/About", "https://example.com/About")] // scheme+host lowercased, path case kept
    [InlineData("https://example.com:443/a", "https://example.com/a")]     // default port removed
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")] // non-default port kept
    [InlineData("https://example.com", "https://example.com/")]            // empty path becomes "/"
    [InlineData("  /about  ", "https://example.com/about")]                // surrounding whitespace
    public void Canonicalizes_equivalent_spellings(string href, string expected)
    {
        Assert.True(UrlNormalizer.TryNormalize(href, Page, out var result));
        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("#top")]                     // in-page anchor
    [InlineData("mailto:hi@example.com")]
    [InlineData("tel:+1-555-0100")]
    [InlineData("javascript:void(0)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("data:text/html,hello")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_links_that_are_not_http_pages(string? href)
    {
        Assert.False(UrlNormalizer.TryNormalize(href, Page, out _));
    }

    [Theory]
    [InlineData("https://example.com/path#x", true)]
    [InlineData("example.com", false)]       // no scheme → not absolute
    [InlineData("ftp://example.com", false)]
    public void Normalizes_absolute_start_urls(string url, bool expectedValid)
    {
        Assert.Equal(expectedValid, UrlNormalizer.TryNormalizeAbsolute(url, out _));
    }
}
