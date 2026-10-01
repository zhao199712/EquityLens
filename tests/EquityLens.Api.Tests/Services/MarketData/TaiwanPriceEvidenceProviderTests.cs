using System.Net;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.MarketData;

public sealed class TaiwanPriceEvidenceProviderTests
{
    [Theory]
    [InlineData("2887", "2024-08-22")]
    [InlineData("2317", "2025-07-30")]
    public async Task OfficialZeroVolumeAndMissingClose_ConfirmsGapWithoutInventingPrice(string ticker, string day)
    {
        var date = DateOnly.Parse(day);
        var prior = date.AddDays(-1);
        var handler = new Handler(uri =>
        {
            if (uri.Host.Contains("finmind"))
                return Envelope(new[] { new { date = prior.ToString("yyyy-MM-dd") }, new { date = day } });
            return JsonSerializer.Serialize(new { stat = "OK", date = date.ToString("yyyyMMdd"),
                fields = new[] { "日期", "成交股數", "開盤價", "最高價", "最低價", "收盤價" },
                data = new[] { new[] { $"{date.Year - 1911}/{date:MM/dd}", "0", "--", "--", "--", "--" } } });
        });
        var provider = Provider(handler);
        var evidence = await provider.GetEvidenceAsync(new Security { Ticker = ticker, Exchange = "TWSE" }, prior, date,
            [new(prior, 100, 100, 100, 100, null, 100), new(date, 0, 0, 0, 0, null, 0)], false, default);
        Assert.Contains(date, evidence.ConfirmedNoTradeDates);
        Assert.Empty(evidence.Corrections);
        Assert.Contains("TWSE:daily", evidence.EvidenceJson);
    }

    [Theory]
    [InlineData("TWSE", "OK")]
    [InlineData("TPEX", "ok")]
    public async Task OfficialPositiveClose_CorrectsZeroUsingObservedOhlc(string exchange, string stat)
    {
        var day = new DateOnly(2024, 8, 22);
        var handler = new Handler(uri => uri.Host.Contains("finmind")
            ? Envelope(new[] { new { date = "2024-08-22" } })
            : JsonSerializer.Serialize(new { stat,
                fields = new[] { "日期", "成交股數", "開盤價", "最高價", "最低價", "收盤價" },
                data = new[] { new[] { "113/08/22", "1,000", "20", "22", "19", "21" } } }));
        var evidence = await Provider(handler).GetEvidenceAsync(new Security { Ticker = "2887", Exchange = exchange },
            day, day, [new(day, 0, 0, 0, 0, null, 0)], false, default);
        Assert.Equal(21m, evidence.Corrections[day].Close);
        Assert.Equal(exchange + "Official", evidence.Corrections[day].RawSource);
        Assert.Empty(evidence.ConfirmedNoTradeDates);
    }

    [Fact]
    public async Task MissingOfficialRow_IsUnverifiedAndRejected()
    {
        var day = new DateOnly(2024, 8, 22);
        var handler = new Handler(uri => uri.Host.Contains("finmind")
            ? Envelope(new[] { new { date = "2024-08-22" } })
            : """{"stat":"OK","fields":["日期","成交股數","開盤價","最高價","最低價","收盤價"],"data":[]}""");
        await Assert.ThrowsAsync<PriceIntegrityException>(() => Provider(handler).GetEvidenceAsync(
            new Security { Ticker = "2887", Exchange = "TWSE" }, day, day, [new(day, 0, 0, 0, 0, null, 0)], false, default));
    }

    [Theory]
    [InlineData("TWSE", "OK", "股票代號", "資料日期")]
    [InlineData("TPEX", "ok", "代號", "除權息日期")]
    public async Task DirectOfficialCombinedFactor_TakesPriorityWithoutDoubleCounting(string exchange, string stat, string tickerField, string dateField)
    {
        var day = new DateOnly(2025, 8, 1);
        var handler = new Handler(uri =>
        {
            if (uri.Host.Contains("finmind"))
            {
                var query = uri.Query;
                if (query.Contains("TaiwanStockTradingDate")) return Envelope(new[] { new { date = "2025-08-01" } });
                if (query.Contains("TaiwanStockDividendResult"))
                    return Envelope(new[] { new { date = "2025-08-01", stock_id = "2887", before_price = 100, after_price = 90 } });
                return Envelope(Array.Empty<object>());
            }
            Assert.Contains(exchange == "TWSE" ? "TWT49U" : "bulletin/exDailyQ", uri.AbsolutePath);
            var table = new { fields = new[] { dateField, tickerField, "除權息前收盤價", "除權息參考價" },
                data = new[] { new[] { "114/08/01", "2887", "100", "90" } } };
            return exchange == "TPEX" ? JsonSerializer.Serialize(new { stat, tables = new[] { table } })
                : JsonSerializer.Serialize(new { stat, table.fields, table.data });
        });
        var evidence = await Provider(handler).GetEvidenceAsync(new Security { Ticker = "2887", Exchange = exchange },
            day, day, [new(day, 90, 90, 90, 90, null, 100)], true, default);
        var action = Assert.Single(evidence.Actions);
        Assert.Equal(exchange + "_combined", action.Kind);
        Assert.Equal(0.9m, action.OfficialFactor);
        Assert.True(evidence.ActionsComplete);
    }

    [Fact]
    public async Task SuccessfulEmptyOfficialInventory_ConflictingDerivedEventIsRejected()
    {
        var day = new DateOnly(2025, 8, 1);
        var handler = new Handler(uri =>
        {
            if (uri.Host.Contains("finmind"))
            {
                if (uri.Query.Contains("TaiwanStockTradingDate")) return Envelope(new[] { new { date = "2025-08-01" } });
                if (uri.Query.Contains("TaiwanStockDividendResult")) return Envelope(new[]
                    { new { date = "2025-08-01", stock_id = "2887", before_price = 100, after_price = 90 } });
                return Envelope(Array.Empty<object>());
            }
            return """{"stat":"OK","fields":["資料日期","股票代號","除權息前收盤價","除權息參考價"],"data":[]}""";
        });
        await Assert.ThrowsAsync<PriceIntegrityException>(() => Provider(handler).GetEvidenceAsync(
            new Security { Ticker = "2887", Exchange = "TWSE" }, day, day, [new(day, 90, 90, 90, 90, null, 100)], true, default));
    }

    [Theory]
    [InlineData("observed")]
    [InlineData("no_trade")]
    [InlineData("unknown")]
    public async Task LeadingMissingDays_AreCheckedAgainstOfficialPrices(string state)
    {
        var from = new DateOnly(2026, 9, 29);
        var to = from.AddDays(2);
        var handler = new Handler(uri => uri.Host.Contains("finmind")
            ? Envelope(Enumerable.Range(0, 3).Select(i => new { date = from.AddDays(i).ToString("yyyy-MM-dd") }))
            : JsonSerializer.Serialize(new { stat = "OK",
                fields = new[] { "日期", "成交股數", "開盤價", "最高價", "最低價", "收盤價" },
                data = state == "unknown" ? Array.Empty<string[]>() : Enumerable.Range(0, 2).Select(i =>
                    new[] { $"115/{from.AddDays(i):MM/dd}", state == "no_trade" ? "0" : "1000",
                        state == "no_trade" ? "--" : "20", state == "no_trade" ? "--" : "22",
                        state == "no_trade" ? "--" : "19", state == "no_trade" ? "--" : "21" }).ToArray() }));
        var provider = Provider(handler);
        var security = new Security { Ticker = "2330", Exchange = "TWSE" };
        var prices = new ImportedMarketPrice[] { new(to, 100, 100, 100, 100, null, 100) };
        if (state == "unknown")
            await Assert.ThrowsAsync<PriceIntegrityException>(() => provider.GetEvidenceAsync(security, from, to, prices, false, default));
        else
        {
            var evidence = await provider.GetEvidenceAsync(security, from, to, prices, false, default);
            var expectedDates = new[] { from, from.AddDays(1) };
            if (state == "no_trade")
            {
                Assert.Equal(expectedDates, evidence.ConfirmedNoTradeDates.Order().ToArray());
                Assert.Empty(evidence.Corrections);
            }
            else
            {
                Assert.Equal(expectedDates, evidence.Corrections.Keys.Order().ToArray());
                Assert.All(evidence.Corrections.Values, x => Assert.Equal(21m, x.Close));
                Assert.Empty(evidence.ConfirmedNoTradeDates);
            }
        }
    }

    private static string Envelope(object data) => JsonSerializer.Serialize(new { status = 200, data });
    private static TaiwanPriceEvidenceProvider Provider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new FinMindOptions()));
    private sealed class Handler(Func<Uri, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request.RequestUri!), Encoding.UTF8, "application/json") });
    }
}
