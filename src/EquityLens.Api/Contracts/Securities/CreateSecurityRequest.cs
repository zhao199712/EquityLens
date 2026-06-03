namespace EquityLens.Api.Contracts.Securities;

public sealed record CreateSecurityRequest(
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string? Currency,
    string? Isin,
    string? Sector,
    string? Industry);
