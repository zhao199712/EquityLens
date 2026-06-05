namespace EquityLens.Api.Services.MarketData;

public sealed record ExternalSecuritySearchResult(
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string Currency,
    string? Isin,
    string? Sector,
    string? Industry,
    string Source);
