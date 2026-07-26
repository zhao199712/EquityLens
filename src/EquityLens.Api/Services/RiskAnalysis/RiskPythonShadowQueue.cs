using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed record RiskPythonShadowJob(
    Guid JobId,
    Guid ComparisonId,
    Guid BacktestRunId,
    string InputHash,
    string InputKey,
    string ModelVersion,
    DateTime CreatedAtUtc);

public sealed record RiskPythonShadowResultItem(
    string StreamId,
    Guid JobId,
    Guid ComparisonId,
    string Status,
    string? ResultKey,
    long? DurationMs,
    string? ErrorMessage);

public interface IRiskPythonShadowQueue
{
    Task<(RiskPythonShadowJob Job, string StreamId)> EnqueueAsync(
        Guid comparisonId,
        Guid backtestRunId,
        RiskBacktestEngineInput input,
        CancellationToken cancellationToken = default);
    Task<RiskPythonShadowResultItem?> ReadResultAsync(
        string consumerName,
        CancellationToken cancellationToken = default);
    Task<string?> GetResultJsonAsync(string resultKey, CancellationToken cancellationToken = default);
    Task AcknowledgeResultAsync(string streamId, CancellationToken cancellationToken = default);
}

public sealed class RedisRiskPythonShadowQueue : IRiskPythonShadowQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _connection;
    private readonly RiskPythonOptions _options;

    public RedisRiskPythonShadowQueue(
        IConnectionMultiplexer connection,
        IOptions<RiskPythonOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    public async Task<(RiskPythonShadowJob Job, string StreamId)> EnqueueAsync(
        Guid comparisonId,
        Guid backtestRunId,
        RiskBacktestEngineInput input,
        CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var jobId = Guid.NewGuid();
        var canonicalJson = JsonSerializer.Serialize(input, JsonOptions);
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        var inputKey = $"{_options.PayloadKeyPrefix}:input:{jobId:N}";
        var compressed = Compress(canonicalJson);
        await db.StringSetAsync(inputKey, compressed, TimeSpan.FromHours(_options.PayloadTtlHours))
            .WaitAsync(cancellationToken);

        var createdAt = DateTime.UtcNow;
        var entries = new NameValueEntry[]
        {
            new("jobId", jobId.ToString()),
            new("comparisonId", comparisonId.ToString()),
            new("backtestRunId", backtestRunId.ToString()),
            new("operation", "mvewma-fhs-backtest"),
            new("modelVersion", _options.CandidateAlgorithmVersion),
            new("inputHash", inputHash),
            new("inputKey", inputKey),
            new("attempt", "1"),
            new("createdAtUtc", createdAt.ToString("o", CultureInfo.InvariantCulture)),
        };
        var streamId = await db.StreamAddAsync(_options.JobsStreamKey, entries).WaitAsync(cancellationToken);
        return (new RiskPythonShadowJob(
            jobId, comparisonId, backtestRunId, inputHash, inputKey,
            _options.CandidateAlgorithmVersion, createdAt), streamId!);
    }

    public async Task<RiskPythonShadowResultItem?> ReadResultAsync(
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await EnsureResultGroupAsync(db, cancellationToken);
        var results = await db.StreamReadGroupAsync(
            _options.ResultsStreamKey,
            _options.ResultsConsumerGroup,
            consumerName,
            count: 1).WaitAsync(cancellationToken);
        if (results.Length == 0) return null;
        var entry = results[0];
        var values = entry.Values.ToDictionary(
            pair => pair.Name.ToString(),
            pair => pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        if (!Guid.TryParse(values.GetValueOrDefault("jobId"), out var jobId) ||
            !Guid.TryParse(values.GetValueOrDefault("comparisonId"), out var comparisonId))
        {
            await AcknowledgeResultAsync(entry.Id!, cancellationToken);
            return null;
        }
        return new RiskPythonShadowResultItem(
            entry.Id!,
            jobId,
            comparisonId,
            values.GetValueOrDefault("status") ?? "Failed",
            values.GetValueOrDefault("resultKey"),
            long.TryParse(values.GetValueOrDefault("durationMs"), out var duration) ? duration : null,
            values.GetValueOrDefault("error"));
    }

    public async Task<string?> GetResultJsonAsync(
        string resultKey,
        CancellationToken cancellationToken = default)
    {
        var value = await _connection.GetDatabase().StringGetAsync(resultKey).WaitAsync(cancellationToken);
        if (value.IsNullOrEmpty) return null;
        return Decompress((byte[])value!);
    }

    public async Task AcknowledgeResultAsync(string streamId, CancellationToken cancellationToken = default) =>
        await _connection.GetDatabase().StreamAcknowledgeAsync(
            _options.ResultsStreamKey, _options.ResultsConsumerGroup, streamId).WaitAsync(cancellationToken);

    private async Task EnsureResultGroupAsync(IDatabase db, CancellationToken cancellationToken)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                _options.ResultsStreamKey,
                _options.ResultsConsumerGroup,
                createStream: true).WaitAsync(cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
        {
        }
    }

    private static byte[] Compress(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            writer.Write(value);
        return output.ToArray();
    }

    private static string Decompress(byte[] value)
    {
        using var input = new MemoryStream(value);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
