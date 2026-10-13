using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

public interface ICrawlJobPublisher
{
    Task PublishJobRequestedAsync(CrawlJobRequested message, CancellationToken ct);
}

/// <summary>
/// Publishes with publisher confirms: the await completes only once RabbitMQ has stored the
/// message, and throws if it could not be stored or routed — so "202 Accepted" from the API
/// means the message really exists.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnection connection) : ICrawlJobPublisher, IAsyncDisposable
{
    public const string RetryCountHeader = "x-retry-count";
    public const string ErrorHeader = "x-error";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public Task PublishJobRequestedAsync(CrawlJobRequested message, CancellationToken ct) =>
        PublishAsync(RabbitMqTopology.Exchange, RabbitMqTopology.JobsRoutingKey, message.Serialize(),
            NewProperties(message.JobId, messageId: Guid.NewGuid().ToString(), retryCount: 0), ct);

    /// <summary>Re-publishes a failed delivery into the delay queue for its attempt number.</summary>
    public Task PublishRetryAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties original, int retryCount,
        string retryQueue, CancellationToken ct)
    {
        var properties = NewProperties(ParseJobId(original), original.MessageId, retryCount);
        return PublishAsync("", retryQueue, body, properties, ct); // default exchange routes by queue name
    }

    public Task PublishDeadLetterAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties original, int retryCount,
        string reason, CancellationToken ct)
    {
        var properties = NewProperties(ParseJobId(original), original.MessageId, retryCount);
        properties.Headers![ErrorHeader] = reason;
        properties.Headers["x-dead-lettered-at"] = DateTimeOffset.UtcNow.ToString("O");
        return PublishAsync("", RabbitMqTopology.DeadLetterQueue, body, properties, ct);
    }

    private async Task PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body,
        BasicProperties properties, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        var conn = await connection.GetAsync(ct);
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        return _channel;
    }

    private static BasicProperties NewProperties(Guid jobId, string? messageId, int retryCount) => new()
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent, // written to disk, survives a broker restart
        Type = CrawlJobRequested.MessageType,
        MessageId = messageId,
        CorrelationId = jobId.ToString(),        // ties logs across API → broker → worker
        Headers = new Dictionary<string, object?> { [RetryCountHeader] = retryCount },
    };

    private static Guid ParseJobId(IReadOnlyBasicProperties properties) =>
        Guid.TryParse(properties.CorrelationId, out var id) ? id : Guid.Empty;

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        _lock.Dispose();
    }
}
