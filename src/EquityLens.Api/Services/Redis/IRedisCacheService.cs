namespace EquityLens.Api.Services.Redis;

/// <summary>
/// Redis 快取服務介面，提供物件序列化、存取與刪除功能。
/// </summary>
public interface IRedisCacheService
{
    /// <summary>
    /// 依據金鑰從 Redis 取得快取資料，並反序列化為指定類型。
    /// </summary>
    /// <typeparam name="T">快取資料的目標類型。</typeparam>
    /// <param name="key">快取金鑰。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>若存在則返回反序列化後的物件，否則返回 null。</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 將物件序列化後寫入 Redis 快取，並設定存留時間。
    /// </summary>
    /// <typeparam name="T">快取資料的類型。</typeparam>
    /// <param name="key">快取金鑰。</param>
    /// <param name="value">要寫入的物件。</param>
    /// <param name="expiry">存留時間；若為 null 則使用設定中的預設值。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 從 Redis 刪除指定金鑰。
    /// </summary>
    /// <param name="key">要刪除的快取金鑰。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依據模式刪除多個符合條件的快取金鑰。
    /// </summary>
    /// <param name="pattern">金鑰模式，例如 "equitylens:cache:portfolio-valuation:*"。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}
