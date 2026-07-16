namespace EquityLens.Api.Contracts.PortfolioCashFlows;

public sealed record UpsertCashFlowRequest(string FlowType, decimal Amount, DateOnly EffectiveDate, string? Note);
