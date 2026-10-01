namespace EquityLens.Api.Services.MarketPrices;

public sealed class PriceIntegrityException(string message) : Exception(message);
