namespace EquityLens.Api.Services.MarketData;

public sealed class FinMindOptions
{
    public string BaseUrl { get; set; } = "https://api.finmindtrade.com";
    public string? Token { get; set; }
}
