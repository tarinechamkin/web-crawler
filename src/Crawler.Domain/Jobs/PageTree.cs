namespace Crawler.Domain.Jobs;

public sealed record PageTreeNode(CrawledPage Page, IReadOnlyList<PageTreeNode> Children);

public static class PageTree
{
    /// <summary>
    /// Builds the discovery tree from the flat page list: each page hangs under the page that first
    /// discovered it (ParentPageId). The crawl graph can have cycles and several parents per page;
    /// the tree is its breadth-first spanning tree, while page_links still holds every edge.
    /// Returns null when the job has no pages yet.
    /// </summary>
    public static PageTreeNode? Build(IReadOnlyCollection<CrawledPage> pages)
    {
        var root = pages.FirstOrDefault(p => p.ParentPageId is null);
        if (root is null)
            return null;

        var childrenByParent = pages
            .Where(p => p.ParentPageId is not null)
            .ToLookup(p => p.ParentPageId!.Value);

        return BuildNode(root, childrenByParent);
    }

    // Recursion depth is bounded by maxDepth (≤ 5), so this cannot overflow the stack.
    private static PageTreeNode BuildNode(CrawledPage page, ILookup<long, CrawledPage> childrenByParent) =>
        new(page, childrenByParent[page.Id]
            .OrderBy(child => child.Url, StringComparer.Ordinal)
            .Select(child => BuildNode(child, childrenByParent))
            .ToList());
}
