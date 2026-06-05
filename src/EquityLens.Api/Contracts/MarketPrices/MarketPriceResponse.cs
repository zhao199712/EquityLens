namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record MarketPriceResponse(
    Guid Id,
    Guid SecurityId,
    DateTime PriceTime,
    string Interval,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? AdjustedClose,
    long? Volume,
    string? DataSource);
