using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// Names and declaration of every exchange/queue. Declaring is idempotent, so both the API and
/// the worker call <see cref="DeclareAsync"/> on startup and whichever starts first creates them.
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "crawler";
    public const string JobsQueue = "crawl-jobs";
    public const string JobsRoutingKey = "crawl-jobs";
    public const string DeadLetterQueue = "crawl-jobs.dlq";

    /// <summary>
    /// Safety net for a message that crashes the worker every time (it never gets to decide):
    /// after this many deliveries RabbitMQ itself dead-letters it.
    /// </summary>
    public const int DeliveryLimit = 5;

    /// <summary>One delay queue per retry attempt (exponential backoff: 5s, 30s, 2min).</summary>
    public static readonly IReadOnlyList<(string Queue, TimeSpan Delay)> RetryQueues =
    [
        ("crawl-jobs.retry.5s", TimeSpan.FromSeconds(5)),
        ("crawl-jobs.retry.30s", TimeSpan.FromSeconds(30)),
        ("crawl-jobs.retry.2m", TimeSpan.FromMinutes(2)),
    ];

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(JobsQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",            // replicated, survives broker restarts
                ["x-delivery-limit"] = DeliveryLimit,
                ["x-dead-letter-exchange"] = "",        // "" = default exchange: route by queue name
                ["x-dead-letter-routing-key"] = DeadLetterQueue,
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(JobsQueue, Exchange, JobsRoutingKey, cancellationToken: ct);

        foreach (var (queue, delay) in RetryQueues)
        {
            // No consumer reads these. A message waits for the TTL, then RabbitMQ dead-letters it
            // back to the main queue — a delayed retry without any scheduler.
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)delay.TotalMilliseconds,
                    ["x-dead-letter-exchange"] = Exchange,
                    ["x-dead-letter-routing-key"] = JobsRoutingKey,
                },
                cancellationToken: ct);
        }
    }
}
