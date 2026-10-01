namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record ImportMarketPricesByTickerResponse(
    Guid SecurityId,
    bool SecurityCreated,
    string Ticker,
    string Exchange,
    string Source,
    int ImportedCount,
    int InsertedCount,
    int UpdatedCount,
    string? AdjustmentSource = null,
    Guid? AdjustmentVersion = null,
    DateOnly? CoverageFrom = null,
    DateOnly? CoverageTo = null,
    DateOnly? VerifiedThrough = null);
