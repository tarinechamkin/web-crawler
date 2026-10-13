using Crawler.Domain.Crawling;

namespace Crawler.Infrastructure.Messaging;

/// <summary>What the consumer should do with a delivery whose processing threw.</summary>
public abstract record FailureDecision
{
    public sealed record Retry(int RetryCount, string RetryQueue) : FailureDecision;
    public sealed record DeadLetter(string Reason) : FailureDecision;
}

/// <summary>
/// Message-level retry rules (page-level HTTP retries happen earlier, inside the fetcher).
/// Permanent: the message is unusable or references a missing job — retrying cannot help.
/// Everything else (database down, broker hiccup, unexpected bug) is treated as possibly transient and
/// retried a bounded number of times with growing delays, then dead-lettered.
/// </summary>
public static class MessageFailurePolicy
{
    public static FailureDecision Decide(Exception error, int retriesSoFar)
    {
        if (error is InvalidMessageException or JobNotFoundException)
            return new FailureDecision.DeadLetter($"Permanent: {error.Message}");

        var retryQueues = RabbitMqTopology.RetryQueues;
        if (retriesSoFar >= retryQueues.Count)
            return new FailureDecision.DeadLetter(
                $"Gave up after {retriesSoFar} retries: {error.GetType().Name}: {error.Message}");

        return new FailureDecision.Retry(retriesSoFar + 1, retryQueues[retriesSoFar].Queue);
    }
}
