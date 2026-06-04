namespace EquityLens.Api.Contracts.DemoData;

public sealed record DemoDataStatusResponse(
    bool IsSeeded,
    int PortfolioCount,
    int HoldingCount,
    int SecurityCount,
    int MarketPriceCount);
