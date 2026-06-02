namespace EquityLens.Api.Contracts.Portfolios;

public sealed record PortfolioListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string BaseCurrency,
    int HoldingCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
