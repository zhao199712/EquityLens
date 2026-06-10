namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record CreatePortfolioHoldingRequest(
    Guid SecurityId,
    decimal Quantity,
    decimal AverageCost,
    string? Note);
