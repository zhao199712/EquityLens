namespace EquityLens.Api.Contracts.Securities;

public sealed record RefreshSecuritiesPricesResponse(
    int Total,
    int Processed,
    int Synced,
    int Skipped,
    int Failed,
    int TotalPricesImported,
    IReadOnlyList<RefreshSecurityPriceFailureResponse> Failures);

public sealed record RefreshSecurityPriceFailureResponse(
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string Error);
