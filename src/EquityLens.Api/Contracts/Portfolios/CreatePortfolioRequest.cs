namespace EquityLens.Api.Contracts.Portfolios;

public sealed record CreatePortfolioRequest(string Name, string? Description, string? BaseCurrency);
