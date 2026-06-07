using System.Globalization;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EquityLens.Api.Services.Redis;

/// <summary>
/// Redis Streams 背景工作佇列實現，支援消費者群組、ack 與 pending 追蹤。
/// </summary>
public sealed class RedisBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisOptions _options;

    /// <summary>
    /// 初始化 Redis 背景工作佇列。
    /// </summary>
    /// <param name="connection">Redis 連線多工器。</param>
    /// <param name="options">Redis 設定選項。</param>
    public RedisBackgroundJobQueue(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default)
    {
        await EnsureConsumerGroupExistsAsync(cancellationToken);

        var db = _connection.GetDatabase();
        var entries = new NameValueEntry[]
        {
            new("jobId", job.JobId.ToString()),
            new("jobType", job.JobType),
            new("correlationId", job.CorrelationId ?? string.Empty),
            new("payload", job.Payload),
            new("createdAtUtc", job.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture))
        };

        var streamId = await db.StreamAddAsync(_options.JobsStreamKey, entries).WaitAsync(cancellationToken);
        return streamId!;
    }

    /// <inheritdoc />
    public async Task<BackgroundJobItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        await EnsureConsumerGroupExistsAsync(cancellationToken);

        var db = _connection.GetDatabase();
        var result = await db.StreamReadGroupAsync(
            _options.JobsStreamKey,
            _options.ConsumerGroupName,
            consumerName,
            count: 1).WaitAsync(cancellationToken);

        if (result.Length == 0)
        {
            return null;
        }

        var entry = result[0];
        var streamId = (string?)entry.Id;
        if (string.IsNullOrWhiteSpace(streamId))
        {
            return null;
        }

        var job = ParseJob(entry);
        return new BackgroundJobItem(streamId, job);
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await db.StreamAcknowledgeAsync(_options.JobsStreamKey, _options.ConsumerGroupName, streamId).WaitAsync(cancellationToken);
    }

    // 確保 Redis Stream 的消費者群組已存在；若 Stream 不存在，會自動建立空 Stream
    private async Task EnsureConsumerGroupExistsAsync(CancellationToken cancellationToken)
    {
        var db = _connection.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(_options.JobsStreamKey, _options.ConsumerGroupName, createStream: true)
                .WaitAsync(cancellationToken);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // 消費者群組已存在，忽略此錯誤
        }
    }

    // 從 StreamEntry 解析出 BackgroundJob
    private static BackgroundJob ParseJob(StreamEntry entry)
    {
        var values = entry.Values
            .Where(x => !x.Name.IsNull)
            .ToDictionary(
                x => (string)x.Name!,
                x => (string?)x.Value,
                StringComparer.OrdinalIgnoreCase);

        var jobId = Guid.TryParse(values.GetValueOrDefault("jobId"), out var parsedId) ? parsedId : Guid.Empty;
        var jobType = values.GetValueOrDefault("jobType") ?? string.Empty;
        var correlationId = values.GetValueOrDefault("correlationId");
        var payload = values.GetValueOrDefault("payload") ?? string.Empty;
        var createdAtUtc = DateTime.TryParseExact(
            values.GetValueOrDefault("createdAtUtc") ?? string.Empty,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsedDate)
            ? parsedDate
            : DateTime.UtcNow;

        return new BackgroundJob(jobId, jobType, correlationId, payload, createdAtUtc);
    }
}
