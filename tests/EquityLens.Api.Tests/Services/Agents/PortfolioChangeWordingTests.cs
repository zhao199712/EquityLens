using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

public class PortfolioChangeWordingTests
{
    [Fact]
    public void SavedScenarioProvidesPositiveMagnitudesWithExplicitDirections()
    {
        var (_, _, comparison) = PortfolioComparisonClaimTests.Fixture();
        var data = PortfolioLoopTools.ScenarioEvidence(comparison);
        Assert.NotNull(data["riskChanges"]);
        var changes = data["riskChanges"]![0]!["changes"]!.AsArray();
        Assert.Equal("年化波動率下降 5.76 個百分點（改善）", changes[0]!["description"]!.ToString());
        Assert.Equal("VaR上升 0.63 個百分點（改善）", changes[1]!["description"]!.ToString());
        Assert.True(changes[0]!["signedChange"]!.GetValue<decimal>() < 0);
        Assert.True(changes[0]!["magnitude"]!.GetValue<decimal>() > 0);
        var text = string.Join("；", changes.Select(c => c!["description"]!.ToString())) + " [P1]";
        var facts = new[] { new LoopDataFact("P1", "scenario", "simulate_weight_scenario", data) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(text, ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new(text.Replace("5.76", "99.99"), ["P1"]), facts));
        Assert.Contains("年化波動率下降 5.76 個百分點（改善）", PortfolioQualityGateNodeHandler.ScenarioSummary(comparison));
    }
    [Fact]
    public void DeteriorationUsesMetricSpecificDirectionAndSavedMetrics()
    {
        var (_, _, comparison) = PortfolioComparisonClaimTests.Fixture();
        var baseline = comparison.Baseline!.Metrics!;
        var scenario = comparison.Scenarios[0] with { Metrics = baseline with {
            AnnualizedVolatility = baseline.AnnualizedVolatility + .01m,
            HistoricalVaR = baseline.HistoricalVaR - .01m,
            ExpectedShortfall = baseline.ExpectedShortfall - .01m,
            HypotheticalMaxDrawdown = baseline.HypotheticalMaxDrawdown - .01m
        } }; // Keep the old DifferenceFromBaseline: descriptions must not trust it.
        var changes = PortfolioComparisonClaims.Changes(comparison, scenario);
        Assert.Equal("年化波動率上升 1.00 個百分點（惡化）", changes[0].Description);
        Assert.All(changes.Skip(1), c => {
            Assert.Equal("下降", c.Direction);
            Assert.Equal("惡化", c.Assessment);
            Assert.Equal(.01m, c.Magnitude);
        });
    }

    [Fact]
    public void UnchangedAndSubPrecisionChangesDoNotClaimVisibleImprovement()
    {
        var (_, _, comparison) = PortfolioComparisonClaimTests.Fixture();
        var baseline = comparison.Baseline!.Metrics!;
        var scenario = comparison.Scenarios[0] with { Metrics = baseline };
        Assert.All(PortfolioComparisonClaims.Changes(comparison, scenario), c => {
            Assert.Equal("持平", c.Assessment);
            Assert.Equal(0m, c.Magnitude);
        });
        scenario = scenario with { Metrics = baseline with { AnnualizedVolatility = baseline.AnnualizedVolatility - .000001m } };
        var change = PortfolioComparisonClaims.Changes(comparison, scenario)[0];
        Assert.Contains("低於顯示精度", change.Description);
        Assert.DoesNotContain("下降 0.00", change.Description);
        Assert.Equal(.000001m, change.Magnitude);
    }

    [Fact]
    public void MissingOrInsufficientMetricsNeverProduceChanges()
    {
        var (_, _, comparison) = PortfolioComparisonClaimTests.Fixture();
        var scenario = comparison.Scenarios[0];
        Assert.Empty(PortfolioComparisonClaims.Changes(comparison with { Status = "InsufficientData" }, scenario));
        Assert.Empty(PortfolioComparisonClaims.Changes(comparison with { Baseline = null }, scenario));
        Assert.Empty(PortfolioComparisonClaims.Changes(comparison, scenario with { Status = "InsufficientData" }));
        Assert.Empty(PortfolioComparisonClaims.Changes(comparison, scenario with { Metrics = null }));
    }
}
