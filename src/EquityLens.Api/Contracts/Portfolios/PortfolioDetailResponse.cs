using EquityLens.Api.Contracts.PortfolioHoldings;

namespace EquityLens.Api.Contracts.Portfolios;

public sealed record PortfolioDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string BaseCurrency,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PortfolioHoldingResponse> Holdings);
