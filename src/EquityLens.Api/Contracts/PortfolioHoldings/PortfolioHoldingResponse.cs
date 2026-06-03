namespace EquityLens.Api.Contracts.PortfolioHoldings;

public sealed record PortfolioHoldingResponse(
    Guid Id,
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string SecurityName,
    decimal Quantity,
    decimal AverageCost,
    string CostCurrency,
    string? Note,
    DateTime UpdatedAtUtc);
