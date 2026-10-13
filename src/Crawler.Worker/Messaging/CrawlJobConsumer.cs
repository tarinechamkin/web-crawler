using Crawler.Domain.Crawling;
using Crawler.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crawler.Worker.Messaging;

/// <summary>
/// Consumes crawl-job messages one at a time (prefetch 1) with manual acks:
/// a message is acked only after it was fully processed, re-routed to a retry queue, or dead-lettered.
/// To scale out, run more worker containers; RabbitMQ spreads messages across them.
/// </summary>
public sealed class CrawlJobConsumer(
    RabbitMqConnection connection,
    RabbitMqPublisher publisher,
    IServiceScopeFactory scopes,
    ILogger<CrawlJobConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await ConnectWithRetryAsync(stoppingToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(RabbitMqTopology.JobsQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming from {Queue}", RabbitMqTopology.JobsQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var properties = delivery.BasicProperties;
        var retries = ReadRetryCount(properties);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = properties.CorrelationId,
            ["MessageId"] = properties.MessageId,
            ["RetryCount"] = retries,
            ["Redelivered"] = delivery.Redelivered,
        });

        try
        {
            var message = CrawlJobRequested.Deserialize(delivery.Body.Span);
            await using (var scope = scopes.CreateAsyncScope())
            {
                var engine = scope.ServiceProvider.GetRequiredService<CrawlEngine>();
                await engine.RunAsync(message.JobId, stoppingToken);
            }
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-crawl: no ack, so RabbitMQ redelivers it once we disconnect.
            logger.LogInformation("Shutdown during processing; message will be redelivered");
        }
        catch (Exception error)
        {
            await HandleFailureAsync(channel, delivery, error, retries);
        }
    }

    private async Task HandleFailureAsync(IChannel channel, BasicDeliverEventArgs delivery, Exception error, int retries)
    {
        var decision = MessageFailurePolicy.Decide(error, retries);
        try
        {
            switch (decision)
            {
                case FailureDecision.Retry retry:
                    logger.LogWarning(error, "Processing failed; scheduling retry {Attempt} via {RetryQueue}",
                        retry.RetryCount, retry.RetryQueue);
                    await publisher.PublishRetryAsync(delivery.Body, delivery.BasicProperties, retry.RetryCount,
                        retry.RetryQueue, CancellationToken.None);
                    break;

                case FailureDecision.DeadLetter deadLetter:
                    logger.LogError(error, "Dead-lettering message: {Reason}", deadLetter.Reason);
                    await publisher.PublishDeadLetterAsync(delivery.Body, delivery.BasicProperties, retries,
                        deadLetter.Reason, CancellationToken.None);
                    await TryMarkJobFailedAsync(delivery.BasicProperties, deadLetter.Reason);
                    break;
            }
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (Exception routingError)
        {
            // Could not move it anywhere (broker trouble): put it back. The quorum queue's delivery
            // limit still guarantees it cannot loop forever.
            logger.LogError(routingError, "Could not route failed message; requeueing it");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    /// <summary>Best effort: the UI should show Failed instead of a job stuck in Pending/Running.</summary>
    private async Task TryMarkJobFailedAsync(IReadOnlyBasicProperties properties, string reason)
    {
        if (!Guid.TryParse(properties.CorrelationId, out var jobId))
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICrawlJobStore>()
                .MarkFailedAsync(jobId, reason, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark job as failed");
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await connection.GetAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("RabbitMQ not reachable yet ({Error}); retrying in 5s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private static int ReadRetryCount(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(RabbitMqPublisher.RetryCountHeader, out var value) == true && value is not null
            ? Convert.ToInt32(value)
            : 0;
}
