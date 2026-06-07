namespace EquityLens.Api.Services.Redis;

/// <summary>
/// 從 Redis Stream 讀取到的待處理任務項目。
/// </summary>
/// <param name="StreamId">Stream 訊息識別碼。</param>
/// <param name="Job">任務內容。</param>
public sealed record BackgroundJobItem(string StreamId, BackgroundJob Job);
