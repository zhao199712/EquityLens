namespace EquityLens.Api.Contracts.Portfolios;

public sealed record UpdatePortfolioRequest(string Name, string? Description, string? BaseCurrency);
