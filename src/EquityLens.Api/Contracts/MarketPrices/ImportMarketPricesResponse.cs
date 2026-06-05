namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record ImportMarketPricesResponse(
    Guid SecurityId,
    string Source,
    int ImportedCount,
    int InsertedCount,
    int UpdatedCount);
