using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// One long-lived connection per process (connections are expensive; channels are cheap).
/// Created lazily, so the service can start before RabbitMQ is reachable; the client library
/// reconnects automatically if the connection drops later.
/// </summary>
public sealed class RabbitMqConnection(IConfiguration config, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private bool _topologyDeclared;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is not null && _topologyDeclared)
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            _connection ??= await CreateConnectionAsync(ct);
            if (!_topologyDeclared)
            {
                await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
                await RabbitMqTopology.DeclareAsync(channel, ct);
                _topologyDeclared = true;
            }
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IConnection> CreateConnectionAsync(CancellationToken ct)
    {
        var uri = config.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:RabbitMq'.");
        var factory = new ConnectionFactory
        {
            Uri = new Uri(uri),
            ClientProvidedName = AppDomain.CurrentDomain.FriendlyName, // shows up in the RabbitMQ UI
            AutomaticRecoveryEnabled = true,
        };
        var connection = await factory.CreateConnectionAsync(ct);
        logger.LogInformation("Connected to RabbitMQ at {Host}", factory.HostName);
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _lock.Dispose();
    }
}
