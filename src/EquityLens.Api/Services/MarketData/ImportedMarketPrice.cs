namespace EquityLens.Api.Services.MarketData;

public sealed record ImportedMarketPrice(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? AdjustedClose,
    long? Volume);
