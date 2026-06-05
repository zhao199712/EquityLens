namespace EquityLens.Api.Contracts.MarketPrices;

public sealed record ImportMarketPricesRequest(DateOnly From, DateOnly To);
