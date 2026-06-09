namespace EquityLens.Api.Contracts.Securities;

public sealed record RefreshSecuritiesResponse(
    int Total,
    int Processed,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<RefreshSecurityFailureResponse> Failures);

public sealed record RefreshSecurityFailureResponse(
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string Error);
