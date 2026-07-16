namespace EquityLens.Api.Contracts.PortfolioDividends;

public sealed record UpdateDividendCashFlowRequest(decimal? Amount, bool? Skip, string? Note);
