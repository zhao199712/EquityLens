namespace EquityLens.Api.Contracts.Securities;

public sealed record SecuritySearchResult(
    Guid? SecurityId,
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string Currency,
    string? Isin,
    string? Sector,
    string? Industry,
    string Source);
