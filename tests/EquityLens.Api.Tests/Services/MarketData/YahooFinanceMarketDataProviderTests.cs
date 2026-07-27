using System.Net;
using System.Text;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Tests.Services.MarketData;

public sealed class YahooFinanceMarketDataProviderTests
{
    [Theory]
    [InlineData("TWSE", "2330.TW")]
    [InlineData("TPEX", "6488.TWO")]
    public async Task GetDailyPricesAsync_TaiwanExchange_MapsToYahooSymbol(string exchange, string expectedSymbol)
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com") };
        var provider = new YahooFinanceMarketDataProvider(httpClient);
        var security = new Security { Ticker = expectedSymbol.Split('.')[0], Exchange = exchange };

        await provider.GetDailyPricesAsync(security, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21), default);

        Assert.Contains($"/v8/finance/chart/{expectedSymbol}", handler.LastRequestUri);
    }

    [Fact]
    public async Task GetDailyPricesAsync_TwseSecurity_ReturnsAdjustedClose()
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com") };
        var provider = new YahooFinanceMarketDataProvider(httpClient);
        var security = new Security { Ticker = "2330", Exchange = "TWSE" };

        var prices = await provider.GetDailyPricesAsync(security, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21), default);

        Assert.Equal(2, prices.Count);
        Assert.Equal(2320.5m, prices[0].AdjustedClose);
        Assert.Equal(2410.25m, prices[1].AdjustedClose);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string LastRequestUri { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri!.PathAndQuery;
            var payload = """
                {
                  "chart": {
                    "result": [{
                      "timestamp": [1784505600, 1784592000],
                      "indicators": {
                        "quote": [{
                          "open": [2320.0, 2410.0],
                          "high": [2330.0, 2420.0],
                          "low": [2310.0, 2400.0],
                          "close": [2325.0, 2415.0],
                          "volume": [1000, 2000]
                        }],
                        "adjclose": [{ "adjclose": [2320.5, 2410.25] }]
                      }
                    }],
                    "error": null
                  }
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
