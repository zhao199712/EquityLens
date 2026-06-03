namespace EquityLens.Api.Contracts.Securities;

public sealed record SecurityResponse(
    Guid Id,
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string Currency,
    string? Isin,
    string? Sector,
    string? Industry);
