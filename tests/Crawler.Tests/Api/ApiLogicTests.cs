using Crawler.Api.Jobs;
using Crawler.Domain.Jobs;

namespace Crawler.Tests.Api;

public class PageTreeTests
{
    private static CrawledPage Page(long id, string path, long? parent, int depth) =>
        new() { Id = id, Url = "https://example.com" + path, ParentPageId = parent, Depth = depth };

    [Fact]
    public void Nests_pages_under_the_page_that_discovered_them()
    {
        var pages = new[]
        {
            Page(3, "/blog/post", parent: 2, depth: 2),
            Page(1, "/", parent: null, depth: 0),
            Page(2, "/blog", parent: 1, depth: 1),
            Page(4, "/about", parent: 1, depth: 1),
        };

        var root = PageTree.Build(pages)!;

        Assert.Equal(1, root.Page.Id);
        Assert.Equal(["https://example.com/about", "https://example.com/blog"], root.Children.Select(c => c.Page.Url));
        var blog = root.Children.Single(c => c.Page.Id == 2);
        Assert.Equal(3, Assert.Single(blog.Children).Page.Id);
    }

    [Fact]
    public void No_pages_means_no_tree()
    {
        Assert.Null(PageTree.Build([]));
    }
}

public class HistoryCursorTests
{
    [Fact]
    public void Round_trips()
    {
        var cursor = new HistoryCursor(new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero), Guid.NewGuid());
        Assert.True(HistoryCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    public void Rejects_garbage(string value)
    {
        Assert.False(HistoryCursor.TryDecode(value, out _));
    }
}
