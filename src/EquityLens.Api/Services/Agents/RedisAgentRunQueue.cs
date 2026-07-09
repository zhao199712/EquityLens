using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EquityLens.Api.Services.Agents;

public sealed class RedisAgentRunQueue : IAgentRunQueue
{
    private const string FirstStreamId = "0-0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _connection;
    private readonly AgentRunQueueOptions _options;

    public RedisAgentRunQueue(IConnectionMultiplexer connection, IOptions<AgentRunQueueOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    public async Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureConsumerGroupExistsAsync(cancellationToken);

        var db = _connection.GetDatabase();
        var entries = new NameValueEntry[]
        {
            new("runId", message.RunId.ToString("D")),
            new("userId", message.UserId.ToString("D")),
            new("workflowType", message.WorkflowType),
            new("enqueuedAtUtc", message.EnqueuedAtUtc.ToString("o", CultureInfo.InvariantCulture)),
            new("payload", JsonSerializer.Serialize(message, SerializerOptions))
        };

        await db.StreamAddAsync(_options.StreamKey, entries).WaitAsync(cancellationToken);
    }

    public async Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        await EnsureConsumerGroupExistsAsync(cancellationToken);

        var db = _connection.GetDatabase();
        var result = await db.StreamReadGroupAsync(
            _options.StreamKey,
            _options.ConsumerGroupName,
            consumerName,
            count: 1).WaitAsync(cancellationToken);

        return result.Length == 0 ? null : ParseQueueItem(result[0]);
    }

    public async Task<AgentRunQueueItem?> ReadStalePendingAsync(
        string consumerName,
        TimeSpan minIdleTime,
        CancellationToken cancellationToken = default)
    {
        await EnsureConsumerGroupExistsAsync(cancellationToken);

        var db = _connection.GetDatabase();
        var result = await db.StreamAutoClaimAsync(
            _options.StreamKey,
            _options.ConsumerGroupName,
            consumerName,
            (long)minIdleTime.TotalMilliseconds,
            FirstStreamId,
            count: 1).WaitAsync(cancellationToken);

        if (result.IsNull || result.ClaimedEntries.Length == 0)
        {
            return null;
        }

        return ParseQueueItem(result.ClaimedEntries[0]);
    }

    public async Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await db.StreamAcknowledgeAsync(_options.StreamKey, _options.ConsumerGroupName, streamId).WaitAsync(cancellationToken);
    }

    private async Task EnsureConsumerGroupExistsAsync(CancellationToken cancellationToken)
    {
        var db = _connection.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(_options.StreamKey, _options.ConsumerGroupName, createStream: true)
                .WaitAsync(cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static AgentRunQueueItem? ParseQueueItem(StreamEntry entry)
    {
        var streamId = (string?)entry.Id;
        if (string.IsNullOrWhiteSpace(streamId)) return null;

        var values = entry.Values
            .Where(x => !x.Name.IsNull)
            .ToDictionary(
                x => (string)x.Name!,
                x => (string?)x.Value,
                StringComparer.OrdinalIgnoreCase);

        AgentRunQueueMessage? message = null;
        if (values.TryGetValue("payload", out var payload) && !string.IsNullOrWhiteSpace(payload))
        {
            message = JsonSerializer.Deserialize<AgentRunQueueMessage>(payload, SerializerOptions);
        }

        message ??= new AgentRunQueueMessage(
            Guid.Parse(values["runId"]!),
            Guid.Parse(values["userId"]!),
            values.GetValueOrDefault("workflowType") ?? string.Empty,
            DateTime.TryParseExact(
                values.GetValueOrDefault("enqueuedAtUtc") ?? string.Empty,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var enqueuedAtUtc)
                ? enqueuedAtUtc
                : DateTime.UtcNow);

        return new AgentRunQueueItem(streamId, message);
    }
}
