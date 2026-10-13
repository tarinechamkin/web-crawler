using System.Text;
using Crawler.Infrastructure.Http;

namespace Crawler.Tests.Http;

public class HttpPageFetcherTests
{
    private static readonly Uri Page = new("https://quotes.toscrape.com/author/Albert-Einstein");

    /// <summary>Parses a Location header the way HttpClient does for a real response.</summary>
    private static Uri? ParseLocationHeader(string value)
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Location", value);
        return response.Headers.Location;
    }

    [Fact]
    public void Explains_an_https_to_http_downgrade()
    {
        var message = HttpPageFetcher.DescribeUnfollowedRedirect(
            Page, 308, ParseLocationHeader("http://quotes.toscrape.com/author/Albert-Einstein/"));

        Assert.Equal(
            "Redirect not followed: HTTPS to HTTP downgrade (http://quotes.toscrape.com/author/Albert-Einstein/)",
            message);
    }

    [Fact]
    public void Resolves_a_relative_location_header()
    {
        // On Linux "/next" parses as file:///next; the message must still show the real URL.
        var message = HttpPageFetcher.DescribeUnfollowedRedirect(Page, 301, ParseLocationHeader("/next"));
        Assert.EndsWith("to https://quotes.toscrape.com/next", message);
    }

    [Fact]
    public void Handles_a_missing_location()
    {
        Assert.Equal("HTTP 302 redirect without a Location header",
            HttpPageFetcher.DescribeUnfollowedRedirect(Page, 302, null));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("\"UTF-8\"")]
    [InlineData("no-such-charset")] // falls back to UTF-8 instead of throwing
    [InlineData(null)]
    public void Decodes_utf8_bodies(string? charset)
    {
        Assert.Equal("café", HttpPageFetcher.DecodeBody(Encoding.UTF8.GetBytes("café"), charset));
    }

    [Fact]
    public void Decodes_legacy_charsets()
    {
        var windows1252 = new byte[] { 0x63, 0x61, 0x66, 0xE9 }; // "café" in windows-1252
        Assert.Equal("café", HttpPageFetcher.DecodeBody(windows1252, "windows-1252"));
    }
}
