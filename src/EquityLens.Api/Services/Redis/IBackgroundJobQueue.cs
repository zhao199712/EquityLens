namespace EquityLens.Api.Services.Redis;

/// <summary>
/// Redis 背景工作佇列介面，使用 Redis Streams 實現任務排程與消費。
/// </summary>
public interface IBackgroundJobQueue
{
    /// <summary>
    /// 將任務加入 Redis Stream 佇列。
    /// </summary>
    /// <param name="job">要加入的任務。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>Stream 訊息識別碼。</returns>
    Task<string> EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// 從 Redis Stream 讀取下一個待處理任務。
    /// </summary>
    /// <param name="consumerName">消費者名稱，建議使用處理程序或執行個體識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>下一個待處理任務；若無則返回 null。</returns>
    Task<BackgroundJobItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 確認任務已處理完成，從 pending 列表中移除。
    /// </summary>
    /// <param name="streamId">Stream 訊息識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default);
}
