namespace EquityLens.Api.Contracts.Securities;

public sealed record ResolveSecurityRequest(
    Guid? SecurityId,
    string? Ticker,
    string? Exchange);
