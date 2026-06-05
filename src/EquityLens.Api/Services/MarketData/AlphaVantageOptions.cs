namespace EquityLens.Api.Services.MarketData;

public sealed class AlphaVantageOptions
{
    public string BaseUrl { get; set; } = "https://www.alphavantage.co";
    public string? ApiKey { get; set; }
}
