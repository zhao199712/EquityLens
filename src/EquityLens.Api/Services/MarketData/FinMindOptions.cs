namespace EquityLens.Api.Services.MarketData;

public sealed class FinMindOptions
{
    public string BaseUrl { get; set; } = "https://api.finmindtrade.com";
    /// <summary>僅讀取已匯入的基準資料，供離線開發與固定資料驗證使用。</summary>
    public bool CachedOnly { get; set; }
    public string? Token { get; set; }
}
