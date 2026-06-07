namespace EquityLens.Api.Services.Redis;

/// <summary>
/// Redis 連線與行為設定選項。
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// Redis 伺服器連線字串，例如 "localhost:6379"。
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Stream 任務佇列使用的金鑰名稱，預設為 "equitylens:jobs"。
    /// </summary>
    public string JobsStreamKey { get; set; } = "equitylens:jobs";

    /// <summary>
    /// Stream 消費者群組名稱，預設為 "equitylens-workers"。
    /// </summary>
    public string ConsumerGroupName { get; set; } = "equitylens-workers";

    /// <summary>
    /// 快取金鑰前綴，預設為 "equitylens:cache"。
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "equitylens:cache";

    /// <summary>
    /// 預設快取存留時間（分鐘），預設為 15 分鐘。
    /// </summary>
    public int DefaultCacheMinutes { get; set; } = 15;
}
