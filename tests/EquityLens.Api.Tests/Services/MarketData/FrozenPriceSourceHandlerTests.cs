using System.Net;
using System.Text.Json;

namespace EquityLens.Api.Tests.Services.MarketData;

public sealed class FrozenPriceSourceHandlerTests
{
    [Fact]
    public async Task MonthlyEvidenceBeforeActionEvidence_DoesNotRequireDateOnActionRequest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "price-replay-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var official = JsonSerializer.Serialize(new object[]
            {
                new { source = "TWSE:daily", month = "2025-07-01", snapshot = "monthly" },
                new { source = "TWSE", from = "2021-10-01", to = "2026-09-30", snapshot = "actions" }
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "2317.json"), JsonSerializer.Serialize(new
                { ticker = "2317", official, captured_at_utc = "2026-10-01T08:00:00Z" }));
            using var handler = new FrozenPriceSourceHandler(directory);
            using var http = new HttpClient(handler);
            handler.Select("2317");
            using var events = await http.GetAsync("https://www.twse.com.tw/exchangeReport/TWT49U?startDate=20211001&endDate=20260930");
            Assert.Equal("actions", await events.Content.ReadAsStringAsync());
            Assert.Contains("frameSha256", Assert.Single(events.Headers.GetValues("X-EquityLens-Replay-Origin")));
            using var month = await http.GetAsync("https://www.twse.com.tw/exchangeReport/STOCK_DAY?date=20250701");
            Assert.Equal("monthly", await month.Content.ReadAsStringAsync());
            using var missing = await http.GetAsync("https://www.twse.com.tw/exchangeReport/STOCK_DAY?date=20250801");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
            handler.Select("missing");
            using var other = await http.GetAsync("https://www.twse.com.tw/exchangeReport/TWT49U");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
