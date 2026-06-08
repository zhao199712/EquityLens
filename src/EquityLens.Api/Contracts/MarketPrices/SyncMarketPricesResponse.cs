namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record SyncMarketPricesResponse(
    Guid SecurityId,
    string? Source,
    bool Synced,
    bool Skipped,
    string? Reason,
    DateOnly? From,
    DateOnly? To,
    int ReceivedCount,
    int InsertedCount,
    int UpdatedCount);
