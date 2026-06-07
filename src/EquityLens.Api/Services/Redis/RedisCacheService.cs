using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EquityLens.Api.Services.Redis;

/// <summary>
/// Redis 快取服務實現，使用 JSON 序列化與 String 資料結構儲存快取資料。
/// </summary>
public sealed class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisOptions _options;

    /// <summary>
    /// 初始化 Redis 快取服務。
    /// </summary>
    /// <param name="connection">Redis 連線多工器。</param>
    /// <param name="options">Redis 設定選項。</param>
    public RedisCacheService(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var json = await db.StringGetAsync(key).WaitAsync(cancellationToken);

        var jsonString = (string?)json;
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(jsonString);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var json = JsonSerializer.Serialize(value);
        var ttl = expiry ?? TimeSpan.FromMinutes(_options.DefaultCacheMinutes);

        await db.StringSetAsync(key, json, ttl).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var server = _connection.GetServer(_connection.GetEndPoints().First());
        var db = _connection.GetDatabase();

        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
        {
            await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
        }
    }
}
