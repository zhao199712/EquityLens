using System.Net;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.MarketData;

public sealed class PriceSourceIdentityTests
{
    [Theory]
    [InlineData("2317", "2025-08-01")]
    [InlineData("2330", "invalid")]
    [InlineData("2330", "2025-08-02")]
    public async Task FinMindWrongTickerOrDate_RejectsBeforeUsingPrice(string ticker, string day)
    {
        using var http = new HttpClient(new Handler(JsonSerializer.Serialize(new
            { status = 200, data = new[] { new { stock_id = ticker, date = day } } })))
            { BaseAddress = new Uri("https://api.finmindtrade.com") };
        var provider = new FinMindMarketDataProvider(http, Options.Create(new FinMindOptions()));
        await Assert.ThrowsAsync<PriceIntegrityException>(() => provider.GetDailyPricesAsync(
            new Security { Ticker = "2330", Exchange = "TWSE" }, new(2025, 8, 1), new(2025, 8, 1), default));
    }

    [Fact]
    public async Task YahooWrongSymbol_RejectsInsteadOfTreatingAnotherStockAsFallback()
    {
        using var http = new HttpClient(new Handler("""{"chart":{"result":[{"meta":{"symbol":"2317.TW"}}],"error":null}}"""))
            { BaseAddress = new Uri("https://query1.finance.yahoo.com") };
        var provider = new YahooFinanceMarketDataProvider(http);
        await Assert.ThrowsAsync<PriceIntegrityException>(() => provider.GetAdjustmentSnapshotAsync(
            new Security { Ticker = "2330", Exchange = "TWSE" }, new(2025, 8, 1), new(2025, 8, 1), default));
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
