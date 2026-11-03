using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class HorizonSampleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortfolioHistoricalHorizons_UseTheirOwnWindowCounts(bool filteredSimulation)
    {
        var id = Guid.NewGuid();
        var returns = Enumerable.Range(0, 120).Select(i => (decimal)Math.Sin(i) / 100).ToArray();
        var prices = new List<decimal> { 100m };
        foreach (var value in returns) prices.Add(prices[^1] * (decimal)Math.Exp((double)value));
        var aligned = new Dictionary<Guid, IReadOnlyList<decimal>> { [id] = prices };
        var weights = new Dictionary<Guid, decimal> { [id] = 1m };
        IReadOnlyList<RiskHorizonResult> results = filteredSimulation
            ? PortfolioRiskModelCalculator.BuildPortfolioHorizonsMvewmaFhs(returns, aligned, weights, 1000, 100, .95m, .99m, out _, out _)
            : PortfolioRiskModelCalculator.BuildPortfolioHorizons(returns, aligned, weights, 1000, 100, .95m);
        foreach (var result in results)
        {
            Assert.Equal(121 - result.HorizonDays, result.EffectiveHistoricalObservations);
            Assert.Equal(100, result.MinimumHistoricalObservations);
            if (result.EffectiveHistoricalObservations < 100)
            {
                Assert.Null(result.HistoricalVaR);
                Assert.Null(result.HistoricalES);
                Assert.Equal("InsufficientData", result.HistoricalStatus);
            }
            else Assert.NotNull(result.HistoricalVaR);
        }
    }
}
