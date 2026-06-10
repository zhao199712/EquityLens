namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record UpdatePortfolioHoldingRequest(
    decimal Quantity,
    decimal AverageCost,
    string? Note);
