namespace EquityLens.Api.Contracts.Portfolios;

/// <summary>
/// 投資組合歷史估值序列。
/// </summary>
/// <param name="PortfolioId">投資組合唯一識別碼。</param>
/// <param name="From">估值起始日期。</param>
/// <param name="To">估值結束日期。</param>
/// <param name="Currency">投資組合基準幣別。</param>
/// <param name="Points">每日估值點。</param>
public sealed record PortfolioValuationHistoryResponse(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PortfolioValuationHistoryPoint> Points);

/// <summary>
/// 單日投資組合估值點。
/// </summary>
/// <param name="Date">估值日期。</param>
/// <param name="TotalCostValue">總成本。</param>
/// <param name="TotalMarketValue">總市值。</param>
/// <param name="TotalUnrealizedPnl">未實現損益。</param>
/// <param name="TotalUnrealizedPnlPercent">未實現損益率。</param>
/// <param name="HoldingCount">當日持倉數量。</param>
/// <param name="PricedHoldingCount">當日有市場價格的持倉數量。</param>
public sealed record PortfolioValuationHistoryPoint(
    DateOnly Date,
    decimal TotalCostValue,
    decimal TotalMarketValue,
    decimal TotalUnrealizedPnl,
    decimal? TotalUnrealizedPnlPercent,
    int HoldingCount,
    int PricedHoldingCount);
