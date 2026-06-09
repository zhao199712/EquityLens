namespace EquityLens.Api.Contracts.Portfolios;

public sealed record HoldingValuationResponse(
    Guid HoldingId,
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string SecurityName,
    decimal Quantity,
    decimal AverageCost,
    string CostCurrency,
    decimal? LatestPrice,
    DateTime? PriceTime,
    decimal CostValue,
    decimal? MarketValue,
    decimal? UnrealizedPnl,
    decimal? UnrealizedPnlPercent,
    decimal? Weight,
    string ValuationStatus);
