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

    private static PortfolioHoldingResponse Holding(Guid id) => new(Guid.NewGuid(), id, "2327", "TWSE", "國巨", 1, 100, "TWD", null, DateTime.UtcNow, null, null);
    private static MarketPriceResponse Price(Guid id, DateTime date, decimal price) => new(Guid.NewGuid(), id, date, "1d", price, price, price, price, price, null, "fixture");
}
