namespace EquityLens.Api.Contracts.DemoData;

public sealed record ClearDemoDataResponse(
    int PortfoliosRemoved,
    int HoldingsRemoved);
