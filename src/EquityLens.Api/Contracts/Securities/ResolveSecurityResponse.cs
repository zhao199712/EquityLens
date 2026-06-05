namespace EquityLens.Api.Contracts.Securities;

public sealed record ResolveSecurityResponse(
    Guid SecurityId,
    bool Created,
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string Currency,
    string? Isin,
    string? Sector,
    string? Industry);
