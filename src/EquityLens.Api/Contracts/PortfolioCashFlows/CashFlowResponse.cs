namespace EquityLens.Api.Contracts.PortfolioCashFlows;

public sealed record CashFlowResponse(Guid Id, string FlowType, decimal Amount, string Currency, DateOnly EffectiveDate, string Status, bool IsUserAdjusted, string? Note, Guid? SecurityId, bool IsSystemDerived);
