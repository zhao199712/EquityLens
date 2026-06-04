namespace EquityLens.Api.Contracts.DemoData;

public sealed record SeedDemoDataResponse(
    int PortfoliosCreated,
    int HoldingsCreated,
    int SecuritiesCreated,
    int MarketPricesInserted,
    int MarketPricesUpdated);
