namespace EquityLens.Api.Contracts.Securities;

public sealed record EnsureSecurityRequest(
    Guid? SecurityId,
    string? Ticker,
    string? Exchange);
