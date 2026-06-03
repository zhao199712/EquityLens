namespace EquityLens.Api.Contracts.Portfolios;

public sealed record PortfolioValuationResponse(
    Guid PortfolioId,
    DateOnly AsOfDate,
    string Currency,
    decimal TotalCostValue,
    decimal TotalMarketValue,
    decimal TotalUnrealizedPnl,
    decimal? TotalUnrealizedPnlPercent,
    IReadOnlyList<HoldingValuationResponse> Holdings);
