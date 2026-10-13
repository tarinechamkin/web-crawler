using System.Text;
using Crawler.Domain.Crawling;
using Crawler.Infrastructure.Messaging;
using Npgsql;

namespace Crawler.Tests.Messaging;

public class MessageFailurePolicyTests
{
    [Fact]
    public void Invalid_message_goes_straight_to_the_dlq()
    {
        var decision = MessageFailurePolicy.Decide(new InvalidMessageException("bad"), retriesSoFar: 0);
        Assert.IsType<FailureDecision.DeadLetter>(decision);
    }

    [Fact]
    public void Missing_job_goes_straight_to_the_dlq()
    {
        var decision = MessageFailurePolicy.Decide(new JobNotFoundException(Guid.NewGuid()), retriesSoFar: 0);
        Assert.IsType<FailureDecision.DeadLetter>(decision);
    }

    [Theory]
    [InlineData(0, 1, "crawl-jobs.retry.5s")]
    [InlineData(1, 2, "crawl-jobs.retry.30s")]
    [InlineData(2, 3, "crawl-jobs.retry.2m")]
    public void Transient_errors_retry_with_growing_delays(int retriesSoFar, int expectedAttempt, string expectedQueue)
    {
        var decision = MessageFailurePolicy.Decide(new NpgsqlException("connection refused"), retriesSoFar);
        var retry = Assert.IsType<FailureDecision.Retry>(decision);
        Assert.Equal(expectedAttempt, retry.RetryCount);
        Assert.Equal(expectedQueue, retry.RetryQueue);
    }

    [Fact]
    public void Gives_up_after_the_last_retry()
    {
        var decision = MessageFailurePolicy.Decide(new NpgsqlException("still down"), retriesSoFar: 3);
        var deadLetter = Assert.IsType<FailureDecision.DeadLetter>(decision);
        Assert.Contains("Gave up after 3 retries", deadLetter.Reason);
    }
}

public class CrawlJobRequestedTests
{
    [Fact]
    public void Round_trips_as_camel_case_json()
    {
        var message = new CrawlJobRequested(1, Guid.NewGuid(), "https://example.com/", 2, DateTimeOffset.UtcNow);
        var json = Encoding.UTF8.GetString(message.Serialize());

        Assert.Contains("\"jobId\"", json);
        Assert.Equal(message, CrawlJobRequested.Deserialize(message.Serialize()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]                                                                  // no jobId
    [InlineData("""{"schemaVersion":99,"jobId":"3f2a4c1e-0000-0000-0000-000000000001"}""")] // unknown version
    public void Rejects_unusable_messages(string body)
    {
        Assert.Throws<InvalidMessageException>(() => CrawlJobRequested.Deserialize(Encoding.UTF8.GetBytes(body)));
    }
}
