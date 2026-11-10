using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public class PortfolioStressScenarioCalculatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WorstWindow_RejectsInvalidPricesWithoutDividingOrDroppingDates(decimal invalid)
    {
        var id = Guid.NewGuid();
        var series = Enumerable.Range(0, 25).Select(i => Price(id, new DateTime(2026, 1, 1).AddDays(i), i == 0 ? invalid : 100)).ToArray();
        var result = PortfolioStressScenarioCalculator.Calculate([Holding(id)], new Dictionary<Guid, decimal> { [id] = 1 },
            new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>> { [id] = series }, new Dictionary<Guid, decimal> { [id] = 100 });
        var worst = Assert.Single(result, x => x.Id == "worst_20d");
        Assert.Equal("invalid_prices", worst.Status);
        Assert.Empty(worst.Holdings);
    }

    [Fact]
    public void HistoricalEvent_DoesNotUsePricesOutsideTheEvent()
    {
        var id = Guid.NewGuid();
        var result = PortfolioStressScenarioCalculator.Calculate([Holding(id)], new Dictionary<Guid, decimal> { [id] = 1 },
            new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>> { [id] = [Price(id, new(2019, 1, 1), 100), Price(id, new(2026, 1, 1), 200)] },
            new Dictionary<Guid, decimal> { [id] = 200 });
        Assert.All(result.Where(x => x.Type == "historical"), x => Assert.Equal("insufficient_prices", x.Status));
    }

    [Fact]
    public void WorstWindow_TreatsStockSplitAsAdjustedPriceNotCrash()
    {
        // 0050 2025-06 一拆四：實際收盤價 188 → 47，還原價連續。
        var id = Guid.NewGuid();
        var series = Enumerable.Range(0, 40).Select(i => Price(id, new DateTime(2025, 5, 1).AddDays(i),
            close: i < 20 ? 188m : 47m, adjusted: 46m + i * .01m)).ToArray();
        var result = PortfolioStressScenarioCalculator.Calculate([Holding(id)], new Dictionary<Guid, decimal> { [id] = 1 },
            new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>> { [id] = series }, new Dictionary<Guid, decimal> { [id] = 47 });
        var worst = Assert.Single(result, x => x.Id == "worst_20d");
        Assert.Equal("ready", worst.Status);
        Assert.True(worst.TotalImpact > -.01m, $"split must not look like a crash: {worst.TotalImpact}");
        Assert.Contains("還原收盤價", worst.Methodology);
    }

    [Fact]
    public void HistoricalEvent_UsesAdjustedReturnButDisplaysActualClose()
    {
        var id = Guid.NewGuid();
        var result = PortfolioStressScenarioCalculator.Calculate([Holding(id)], new Dictionary<Guid, decimal> { [id] = 1 },
            new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>> { [id] = [
                Price(id, new(2020, 2, 19), close: 400m, adjusted: 100m), Price(id, new(2020, 3, 23), close: 70m, adjusted: 70m)] },
            new Dictionary<Guid, decimal> { [id] = 70 });
        var covid = Assert.Single(result, x => x.Id == "covid");
        var holding = Assert.Single(covid.Holdings);
        Assert.Equal(-.3m, holding.Shock);
        Assert.Equal(400m, holding.BasePrice);
        Assert.Equal(70m, holding.StressedPrice);
    }

    [Fact]
    public void HistoricalEvent_FallsBackToCloseForWholePairAndDisclosesIt()
    {
        var id = Guid.NewGuid();
        var result = PortfolioStressScenarioCalculator.Calculate([Holding(id)], new Dictionary<Guid, decimal> { [id] = 1 },
            new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>> { [id] = [
                Price(id, new(2020, 2, 19), close: 100m, adjusted: null), Price(id, new(2020, 3, 23), close: 80m, adjusted: 60m)] },
            new Dictionary<Guid, decimal> { [id] = 80 });
        var covid = Assert.Single(result, x => x.Id == "covid");
        Assert.Equal(-.2m, Assert.Single(covid.Holdings).Shock);
        Assert.Contains("改用收盤價", covid.Methodology);
    }

    private static MarketPriceResponse Price(Guid id, DateTime date, decimal close, decimal? adjusted) =>
        new(Guid.NewGuid(), id, date, "1d", close, close, close, close, adjusted, null, "fixture");

    private static PortfolioHoldingResponse Holding(Guid id) => new(Guid.NewGuid(), id, "2327", "TWSE", "國巨", 1, 100, "TWD", null, DateTime.UtcNow, null, null);
    private static MarketPriceResponse Price(Guid id, DateTime date, decimal price) => new(Guid.NewGuid(), id, date, "1d", price, price, price, price, price, null, "fixture");
}
