namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record ImportMarketPricesByTickerRequest(
    string Ticker,
    string Exchange,
    DateOnly From,
    DateOnly To,
    string? Name,
    string? AssetType,
    string? Currency,
    string? Isin,
    string? Sector,
    string? Industry);
