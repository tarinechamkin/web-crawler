namespace Crawler.Api.Jobs;

/// <summary>Limits applied to new jobs. Bound from the "Crawl" configuration section.</summary>
public sealed class CrawlOptions
{
    public int DefaultMaxDepth { get; set; } = 2;
    public int MaxDepthLimit { get; set; } = 5;
    public int MaxPagesPerJob { get; set; } = 200;
}
