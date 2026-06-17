namespace EquityLens.Api.Contracts.Filings;

/// <summary>
/// 批次爬取財報請求。
/// </summary>
public sealed record CrawlFilingsRequest(
    IReadOnlyList<string> StockCodes,
    int StartYear,
    int EndYear);

/// <summary>
/// 批次爬取財報回應。
/// </summary>
public sealed record CrawlFilingsResponse(
    int TotalRequested,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<CrawlFilingsResponse.FailedItem> FailedFiles)
{
    /// <summary>
    /// 爬取失敗的項目。
    /// </summary>
    public sealed record FailedItem(string StockCode, int Year, string Reason);
}
