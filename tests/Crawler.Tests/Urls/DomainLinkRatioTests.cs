using Crawler.Domain.Urls;

namespace Crawler.Tests.Urls;

public class DomainLinkRatioTests
{
    private const string StartHost = "example.com";

    private static Uri[] Links(params string[] urls) => urls.Select(u => new Uri(u)).ToArray();

    [Fact]
    public void Zero_links_gives_zero()
    {
        Assert.Equal(0, DomainLinkRatio.Calculate([], StartHost));
    }

    [Fact]
    public void All_internal_gives_one()
    {
        var links = Links("https://example.com/a", "https://example.com/b");
        Assert.Equal(1, DomainLinkRatio.Calculate(links, StartHost));
    }

    [Fact]
    public void All_external_gives_zero()
    {
        var links = Links("https://other.com/a", "https://twitter.com/b");
        Assert.Equal(0, DomainLinkRatio.Calculate(links, StartHost));
    }

    [Fact]
    public void Mixed_links_give_internal_over_total()
    {
        var links = Links(
            "https://example.com/a",
            "https://example.com/b",
            "http://example.com/c",   // scheme does not matter, only the host
            "https://other.com/d");
        Assert.Equal(0.75, DomainLinkRatio.Calculate(links, StartHost));
    }

    [Fact]
    public void Subdomains_count_as_external()
    {
        var links = Links("https://example.com/a", "https://blog.example.com/b", "https://www.example.com/c");
        Assert.Equal(1.0 / 3, DomainLinkRatio.Calculate(links, StartHost), precision: 10);
    }

    [Fact]
    public void Host_comparison_ignores_case()
    {
        Assert.True(DomainLinkRatio.IsInternal(new Uri("https://example.com/a"), "EXAMPLE.com"));
    }
}
