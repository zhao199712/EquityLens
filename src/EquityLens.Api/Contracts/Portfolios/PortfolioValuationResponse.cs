namespace EquityLens.Api.Contracts.Portfolios;

public sealed record PortfolioValuationResponse(
    Guid PortfolioId,
    DateOnly AsOfDate,
    string Currency,
    decimal TotalCostValue,
    decimal TotalMarketValue,
    decimal TotalUnrealizedPnl,
    decimal? TotalUnrealizedPnlPercent,
    IReadOnlyList<HoldingValuationResponse> Holdings,
    decimal CashBalance = 0m,
    decimal TotalAssetValue = 0m,
    decimal TotalRealizedPnl = 0m,
    decimal? TodayPnl = null,
    decimal? Twr = null,
    decimal? Xirr = null);
