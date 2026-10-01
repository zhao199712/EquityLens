using System.Text.Json;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Tests.Services.MarketData;

public sealed class YahooAdjustedCloseIntegrityTests
{
    private static string Chart(string date, string adjusted, string? secondDate = null) =>
        JsonSerializer.Serialize(new
        {
            chart = new
            {
                result = new[] { new
                {
                    meta = new { exchangeTimezoneName = "Asia/Taipei" },
                    timestamp = secondDate is null ? new[] { Epoch(date) } : new[] { Epoch(date), Epoch(secondDate) },
                    indicators = new
                    {
                        quote = new[] { new { open = new decimal?[] { null }, high = new decimal?[] { null },
                            low = new decimal?[] { null }, close = new decimal?[] { 610 } } },
                        adjclose = new[] { new { adjclose = JsonSerializer.Deserialize<decimal?[]>(adjusted) } }
                    }
                } },
                error = (string?)null
            }
        });
    private static long Epoch(string s) => DateTimeOffset.Parse(s + "T00:00:00+08:00").ToUnixTimeSeconds();

    [Theory]
    [InlineData("2021-04-06", "553.152649")]
    [InlineData("2025-08-01", "1121.702026")]
    public void MissingOhlc_DoesNotDiscardValidAdjustment(string day, string value)
    {
        var date = DateOnly.Parse(day);
        var snapshot = YahooFinanceMarketDataProvider.ParseAdjustmentSnapshot(Chart(day, "[" + value + "]"), "TWSE", date, date);
        Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), snapshot.AdjustedCloses[date]);
    }

    [Theory]
    [InlineData("2021-04-06")]
    [InlineData("2025-08-01")]
    public void ActualSourceNull_RemainsMissing(string day)
    {
        var date = DateOnly.Parse(day);
        var snapshot = YahooFinanceMarketDataProvider.ParseAdjustmentSnapshot(Chart(day, "[null]"), "TWSE", date, date);
        Assert.Empty(snapshot.AdjustedCloses);
    }

    [Theory]
    [InlineData("[0]")]
    [InlineData("[-1]")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    public void NonpositiveOrMismatchedArray_IsRejected(string values)
    {
        var date = new DateOnly(2021, 4, 6);
        Assert.Throws<InvalidOperationException>(() =>
            YahooFinanceMarketDataProvider.ParseAdjustmentSnapshot(Chart("2021-04-06", values), "TWSE", date, date));
    }

    [Fact]
    public void ExchangeLocalMidnight_UsesTaipeiDate()
    {
        var date = new DateOnly(2021, 4, 6);
        var result = YahooFinanceMarketDataProvider.ParseAdjustmentSnapshot(Chart("2021-04-06", "[555]"), "TWSE", date, date);
        Assert.Equal(555m, result.AdjustedCloses[date]);
    }
}
