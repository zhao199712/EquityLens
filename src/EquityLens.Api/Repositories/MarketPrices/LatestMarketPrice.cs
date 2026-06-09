namespace EquityLens.Api.Repositories.MarketPrices;

public sealed record LatestMarketPrice(
    Guid SecurityId,
    decimal Price,
    DateTime PriceTime,
    string? DataSource);
