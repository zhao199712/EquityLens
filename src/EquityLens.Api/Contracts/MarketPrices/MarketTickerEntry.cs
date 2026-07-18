namespace EquityLens.Api.Contracts.MarketPrices;

/// <summary>
/// 行情跑馬燈單筆資料,包含代碼、名稱、最新收盤價與日漲跌幅。
/// </summary>
/// <param name="Code">證券代碼或指數代碼。</param>
/// <param name="Name">顯示名稱。</param>
/// <param name="Close">最新收盤價。</param>
/// <param name="ChangePct">與前一交易日相比的漲跌幅(%)。</param>
public sealed record MarketTickerEntry(string Code, string Name, decimal Close, decimal? ChangePct);
