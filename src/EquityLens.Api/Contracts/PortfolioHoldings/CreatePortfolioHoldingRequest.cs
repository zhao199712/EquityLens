namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record CreatePortfolioHoldingRequest(
    Guid? SecurityId,
    string? Ticker,
    string? Exchange,
    string? Name,
    string? AssetType,
    string? Currency,
    string? Isin,
    string? Sector,
    string? Industry,
    decimal Quantity,
    decimal AverageCost,
    string? CostCurrency,
    string? Note);
