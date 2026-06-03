namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record CreatePortfolioHoldingRequest(
    Guid SecurityId,
    decimal Quantity,
    decimal AverageCost,
    string? CostCurrency,
    string? Note);
