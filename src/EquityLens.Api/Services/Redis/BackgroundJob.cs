namespace EquityLens.Api.Services.Redis;

/// <summary>
/// 背景工作佇列中的任務內容。
/// </summary>
/// <param name="JobId">任務唯一識別碼。</param>
/// <param name="JobType">任務類型，例如 "FinancialReportParse"。</param>
/// <param name="CorrelationId">關聯識別碼，用於追蹤同一請求鏈。</param>
/// <param name="Payload">任務承載的 JSON 字串。</param>
/// <param name="CreatedAtUtc">任務建立時間（UTC）。</param>
public sealed record BackgroundJob(
    Guid JobId,
    string JobType,
    string? CorrelationId,
    string Payload,
    DateTime CreatedAtUtc);
