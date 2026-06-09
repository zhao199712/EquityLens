namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record CreatePortfolioHoldingRequest(
    Guid? SecurityId,
    string? Ticker,
    string? Exchange,
    decimal Quantity,
    decimal AverageCost,
    string? CostCurrency,
    string? Note);
