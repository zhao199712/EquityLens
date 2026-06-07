namespace EquityLens.Api.Contracts.Files;

/// <summary>
/// Presigned URL 下載回應資料。
/// </summary>
/// <param name="Url">限時下載 URL。</param>
/// <param name="ExpiresAtUtc">URL 過期時間（UTC）。</param>
public sealed record PresignedUrlResponse(string Url, DateTime ExpiresAtUtc);
