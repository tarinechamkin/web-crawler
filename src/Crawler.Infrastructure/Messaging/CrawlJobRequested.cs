using System.Text.Json;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// Message published by the API when a job is created; consumed by the worker.
/// The database row stays the source of truth: the worker reads the job by <see cref="JobId"/>.
/// Url/MaxDepth are carried so a message in the DLQ is understandable on its own.
/// </summary>
public sealed record CrawlJobRequested(
    int SchemaVersion,
    Guid JobId,
    string Url,
    int MaxDepth,
    DateTimeOffset RequestedAt)
{
    public const string MessageType = "CrawlJobRequested";
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web); // camelCase

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Throws <see cref="InvalidMessageException"/> for anything we cannot process.</summary>
    public static CrawlJobRequested Deserialize(ReadOnlySpan<byte> body)
    {
        CrawlJobRequested? message;
        try
        {
            message = JsonSerializer.Deserialize<CrawlJobRequested>(body, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidMessageException($"Malformed JSON: {ex.Message}");
        }

        if (message is null || message.JobId == Guid.Empty)
            throw new InvalidMessageException("Missing jobId");
        if (message.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidMessageException($"Unsupported schemaVersion {message.SchemaVersion}");
        return message;
    }
}

/// <summary>The message itself is broken: retrying can never help, so it goes straight to the DLQ.</summary>
public sealed class InvalidMessageException(string reason) : Exception(reason);
