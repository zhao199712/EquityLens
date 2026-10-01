using System.Text.Json;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;

namespace EquityLens.Api.Tests.Services.MarketPrices;

public sealed class PriceAdjustmentChainTests
{
    [Fact]
    public void SharedPythonGoldenFixtures_MatchAtDatabasePrecision()
    {
        using var stream = typeof(PriceAdjustmentChainTests).Assembly.GetManifestResourceStream(
            "EquityLens.Api.Tests.Fixtures.price-adjustment-chain.json")!;
        using var json = JsonDocument.Parse(stream);
        foreach (var c in json.RootElement.EnumerateArray())
        {
            var prices = c.GetProperty("prices").EnumerateArray().Select(x =>
                new ImportedMarketPrice(DateOnly.Parse(x[0].GetString()!), x[1].GetDecimal(), x[1].GetDecimal(),
                    x[1].GetDecimal(), x[1].GetDecimal(), null, 100)).ToList();
            var actions = c.GetProperty("actions").EnumerateArray().Select(x =>
                new CorporatePriceAction(DateOnly.Parse(x[0].GetString()!), x[1].GetString()!, x[2].GetDecimal(),
                    x[3].ValueKind == JsonValueKind.Null ? null : x[3].GetDecimal())).ToList();
            var actual = PriceAdjustmentChain.Rebuild(prices, actions);
            var expected = c.GetProperty("expected").EnumerateArray().Select(x => x.GetDecimal()).ToList();
            for (var i = 0; i < prices.Count; i++) Assert.Equal(expected[i], actual[prices[i].Date]);
        }
    }

    [Theory]
    [InlineData("stock_dividend")]
    [InlineData("capital_reduction")]
    [InlineData("rights_issue")]
    public void NonCashActionWithoutOfficialFactor_IsRejected(string kind)
    {
        var date = new DateOnly(2025, 8, 1);
        Assert.Throws<PriceIntegrityException>(() => PriceAdjustmentChain.Rebuild(
            [new(date, 100, 100, 100, 100, null, 100)], [new(date, kind, 1)]));
    }

    [Fact]
    public void CombinedFactorAndComponentOnSameDate_IsRejected()
    {
        var date = new DateOnly(2025, 8, 1);
        Assert.Throws<PriceIntegrityException>(() => PriceAdjustmentChain.Rebuild(
            [new(date, 100, 100, 100, 100, null, 100)], [new(date, "combined", 0, 0.9m), new(date, "cash", 10)]));
    }
}
