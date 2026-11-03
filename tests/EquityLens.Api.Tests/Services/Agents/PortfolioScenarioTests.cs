using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioScenarioTests
{
    private static PortfolioScenarioSnapshot Snapshot(int days = 253, bool opposite = false)
    {
        var dates = Enumerable.Range(0, days).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).ToArray();
        var a = new List<decimal> { 100m }; var b = new List<decimal> { 100m };
        for (var i = 1; i < days; i++)
        {
            var r = i % 2 == 0 ? .02m : -.01m;
            a.Add(a[^1] * (1 + r)); b.Add(b[^1] * (1 + (opposite ? -r : r)));
        }
        return new(Guid.NewGuid(), DateTime.UtcNow, "TWD", dates[0], dates[^1], dates,
            [new("2327", 50, 100, a), new("2330", 50, 100, b)], 0, []);
    }
    private static PortfolioScenarioRequest Request(decimal target) => new("國巨情境", new Dictionary<string, decimal> { ["2327"] = target });

    [Fact]
    public void ScenarioLabel_DoesNotTrustModelStockNames()
    {
        var named = Snapshot() with { Assets = Snapshot().Assets.Select(a => a with { SecurityName = a.Ticker == "2327" ? "國巨" : "台積電" }).ToArray() };
        var comparison = PortfolioScenarioCalculator.Calculate(named,
            [Request(.3m) with { Name = "國建 2327 轉入台達電", RemainderPolicy = "holding", TransferToTicker = "2330" }]);
        Assert.DoesNotContain("國建", comparison.Scenarios[0].Name);
        Assert.DoesNotContain("台達電", comparison.Scenarios[0].Name);
        Assert.Contains("國巨（2327）", comparison.Scenarios[0].Name);
        Assert.Contains("台積電（2330）", comparison.Scenarios[0].Name);
        Assert.DoesNotContain("國建", PortfolioQualityGateNodeHandler.ScenarioSummary(comparison));
    }

    [Fact]
    public void ScenarioWorkflow_PassesContracts_AndOrdersToolAfterOwnershipCheck()
    {
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid(), new(2025, 1, 1), new(2026, 1, 1), Routing([Request(.3m)]));
        var order = new AgentWorkflowPlanner().GetExecutionOrder(run.WorkflowDefinitionJson);
        new AgentRunGraphValidator().Validate(run, order);
        Assert.Equal(PortfolioDiagnosisNodeKeys.LoadContext, order[0]);
        Assert.Equal("portfolioAgent", order[1]);
        Assert.Equal("portfolioQualityGate", order[2]);
        Assert.Equal(6, run.Nodes.Count);

    }

    [Fact]
    public void TransferReductionToExistingHolding_ConservesNavAndCash()
    {
        var request = JsonSerializer.Deserialize<PortfolioScenarioRequest>("""{"name":"轉入台積電","targetWeights":{"2327":0.3},"remainderPolicy":"holding","preserveOtherHoldings":true,"transferToTicker":"2330"}""", AgentNodeJson.SerializerOptions)!;
        var snapshot = Snapshot() with { Assets = [.. Snapshot().Assets, new("2317", 10, 100, Snapshot().Assets[0].Prices)] };
        var result = PortfolioScenarioCalculator.Calculate(snapshot, [request]).Scenarios.Single();
        Assert.Equal(.3m, result.Weights["2327"]);
        Assert.Equal(10, result.Quantities["2317"]);
        Assert.Equal(0, result.CashWeight);
        Assert.InRange(Math.Abs(11000m - snapshot.Assets.Sum(a => result.Quantities[a.Ticker] * a.ValuationPrice)), 0, .00000001m);
        Assert.InRange(result.Weights["2330"], .609090m, .609091m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("9999")]
    [InlineData("2327")]
    public void TransferRejectsMissingUnknownOrSameDestination(string? receiver)
    {
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(Snapshot(),
            [Request(.3m) with { RemainderPolicy = "holding", TransferToTicker = receiver }]));
    }

    [Fact]
    public void ScenarioReplyUsesSavedDestinationAndWeights()
    {
        var comparison = PortfolioScenarioCalculator.Calculate(Snapshot(), [Request(.3m) with { RemainderPolicy = "holding", TransferToTicker = "2330" }]);
        var reply = PortfolioQualityGateNodeHandler.ScenarioSummary(comparison);
        Assert.Contains("轉入 2330", reply);
        Assert.Contains($"2330 權重 {comparison.Scenarios[0].Weights["2330"]:P2}", reply);
        Assert.Contains("未變更實際持倉", reply);
        Assert.DoesNotContain("留作現金", reply);
        foreach (var metric in new[] { "年化波動", "VaR", "ES", "最大回撤" }) Assert.Contains(metric, reply);
        Assert.Contains("同一快照原組合", reply);
        Assert.Contains("相較原組合差異", reply);
        Assert.Contains($"{comparison.Scenarios[0].Metrics!.ExpectedShortfall:P2}", reply);
    }

    [Fact]
    public void SameWeights_ReproduceBaseline_AndKeepCashInDenominator()
    {
        var result = PortfolioScenarioCalculator.Calculate(Snapshot(), [Request(.5m)]);
        Assert.Equal(result.Baseline!.Metrics!.AnnualizedVolatility, result.Scenarios[0].Metrics!.AnnualizedVolatility);
        Assert.Equal(0, result.Scenarios[0].DifferenceFromBaseline!.AnnualizedVolatility);
        var reduced = PortfolioScenarioCalculator.Calculate(Snapshot(), [Request(.3m)]).Scenarios[0];
        Assert.Equal(.2m, reduced.CashWeight);
        Assert.Equal(50m, reduced.Quantities["2330"]);
        Assert.Equal(30m, reduced.Quantities["2327"]);
        Assert.Equal(.34m, reduced.EquityHhi);
        Assert.Equal(.5m, reduced.LargestEquityWeight);
        Assert.Equal(1m, reduced.Weights.Values.Sum() + reduced.CashWeight);
    }

    [Fact]
    public void CashReduction_ScalesPerfectlyCorrelatedRisk_AndContributionAddsUp()
    {
        var result = PortfolioScenarioCalculator.Calculate(Snapshot(), [Request(.3m)]);
        var metric = result.Scenarios[0].Metrics!;
        Assert.InRange(Math.Abs(metric.AnnualizedVolatility - result.Baseline!.Metrics!.AnnualizedVolatility * .8m), 0, .00000001m);
        Assert.InRange(Math.Abs(metric.Contributions.Sum(c => c.ComponentVolatility) - metric.AnnualizedVolatility), 0, .00000001m);
        Assert.InRange(metric.Contributions.Sum(c => c.ComponentRiskShare), .999999m, 1.000001m);
    }

    [Fact]
    public void ReducingHedge_CanIncreaseRisk_NotAssumedAlwaysBetter()
    {
        var result = PortfolioScenarioCalculator.Calculate(Snapshot(opposite: true), [Request(0)]);
        Assert.True(result.Scenarios[0].Metrics!.AnnualizedVolatility > result.Baseline!.Metrics!.AnnualizedVolatility);
    }

    [Fact]
    public void AllCash_IsZeroRisk_NotRenormalizedToEquities()
    {
        var request = new PortfolioScenarioRequest("全現金", new Dictionary<string, decimal> { ["2327"] = 0, ["2330"] = 0 });
        var result = PortfolioScenarioCalculator.Calculate(Snapshot(), [request]).Scenarios[0];
        Assert.Equal(1, result.CashWeight); Assert.Equal(0, result.EquityHhi);
        Assert.Equal(0, result.Metrics!.AnnualizedVolatility); Assert.Equal(0, result.Metrics.HistoricalVaR);
        Assert.Equal(0, result.Metrics.ExpectedShortfall); Assert.Equal(0, result.Metrics.HypotheticalMaxDrawdown);
    }

    [Fact]
    public void ShortHistory_ProvidesAllocationOnly_AndThresholdIs120Returns()
    {
        var shortResult = PortfolioScenarioCalculator.Calculate(Snapshot(120), [Request(.3m)]);
        Assert.Null(shortResult.Scenarios[0].Metrics); Assert.Equal("InsufficientHistory", shortResult.Status);
        Assert.NotNull(PortfolioScenarioCalculator.Calculate(Snapshot(121), [Request(.3m)]).Scenarios[0].Metrics);
    }

    [Theory]
    [InlineData(-.1)] [InlineData(.6)] [InlineData(1.1)]
    public void RejectsInvalidOrIncreasedWeights(decimal weight) =>
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(Snapshot(), [Request(weight)]));

    [Fact]
    public void RejectsUnknownTickerMissingPricesAndNegativeCash()
    {
        var snapshot = Snapshot();
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.ValidateRequests([null!]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(snapshot, [new("unknown", new Dictionary<string, decimal> { ["9999"] = .1m })]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(snapshot with { Cash = -1 }, [Request(.3m)]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(snapshot with { Assets = [snapshot.Assets[0] with { Prices = [1] }, snapshot.Assets[1]] }, [Request(.3m)]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.ValidateRequests([Request(.3m) with { RemainderPolicy = "redistribute" }]));
    }

    [Fact]
    public void ProvenanceChangesWithHoldingsAndParameters()
    {
        var snapshot = Snapshot();
        var a = PortfolioScenarioCalculator.Calculate(snapshot, [Request(.3m)]);
        Assert.Equal(a.InputHash, PortfolioScenarioCalculator.Calculate(snapshot, [Request(.3m)]).InputHash);
        Assert.NotEqual(a.InputHash, PortfolioScenarioCalculator.Calculate(snapshot, [Request(.2m)]).InputHash);
        Assert.NotEqual(a.InputHash, PortfolioScenarioCalculator.Calculate(snapshot with { Cash = 100 }, [Request(.3m)]).InputHash);
    }



    [Fact]
    public async Task Handler_RejectsOtherUsersPortfolioWithoutWritingTransactions()
    {
        await using var db = new TestDb(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var portfolio = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "private" };
        db.Portfolios.Add(portfolio);
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), portfolio.Id, new(2025, 1, 1), new(2026, 1, 1), Routing([Request(.3m)]));
        var board = JsonNode.Parse(run.BlackboardJson)!.AsObject();
        board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(portfolio.Id, "private", "TWD", new(2025, 1, 1), new(2026, 1, 1), 0), AgentNodeJson.SerializerOptions);
        run.BlackboardJson = board.ToJsonString(); db.AgentRuns.Add(run); await db.SaveChangesAsync();
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "scenarioFixture", NodeType = "PortfolioAgentLoop" };
        db.AgentRunNodes.Add(node);
        await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => PortfolioScenarioSnapshotReader.CaptureAsync(new(db, run, node, (_, _, _, _, _) => { }), default)); Assert.Empty(db.PortfolioTransactions);
    }

    [Theory]
    [InlineData(false, false, "Calculated")]
    [InlineData(true, false, "Unavailable")]
    [InlineData(false, true, "Unavailable")]
    public async Task Handler_CapturesOwnedPricesAndCash_RejectsMissingAssetOrMixedCurrency(bool missingPrices, bool mixedCurrency, string expected)
    {
        await using var db = new TestDb(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var snapshot = Snapshot(); var user = Guid.NewGuid();
        var portfolio = new Portfolio { Id = snapshot.PortfolioId, OwnerUserId = user, Name = "fixture" };
        foreach (var (asset, index) in snapshot.Assets.Select((a, i) => (a, i)))
        {
            var security = new Security { Id = Guid.NewGuid(), Ticker = asset.Ticker, Name = asset.Ticker == "2327" ? "國巨" : "台積電", Currency = mixedCurrency && index == 1 ? "USD" : "TWD" };
            portfolio.Holdings.Add(new PortfolioHolding { Id = Guid.NewGuid(), Security = security, SecurityId = security.Id, Quantity = asset.Quantity, PortfolioId = portfolio.Id });
            if (missingPrices && index == 1) continue;
            foreach (var date in snapshot.Dates)
                db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = security.Id, PriceTime = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Close = 100, AdjustedClose = 100 });
        }
        db.Portfolios.Add(portfolio);
        db.PortfolioCashFlows.Add(new PortfolioCashFlow { Id = Guid.NewGuid(), PortfolioId = portfolio.Id, FlowType = "Deposit", Amount = 2500, Currency = "TWD", EffectiveDate = snapshot.From, Status = "Confirmed" });
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(user, portfolio.Id, snapshot.From, snapshot.To, Routing([Request(.3m)]));
        db.AgentRuns.Add(run); await db.SaveChangesAsync();
        await new LoadPortfolioDiagnosisContextNodeHandler().ExecuteAsync(new(db, run, run.Nodes.Single(n => n.NodeType == PortfolioDiagnosisNodeTypes.LoadContext), (_, _, _, _, _) => { }));
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "scenarioFixture", NodeType = "PortfolioAgentLoop" };
        db.AgentRunNodes.Add(node);
        if (expected == "Unavailable")
            await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => PortfolioScenarioSnapshotReader.CaptureAsync(new(db, run, node, (_, _, _, _, _) => { }), default));
        else
        {
            var captured = await PortfolioScenarioSnapshotReader.CaptureAsync(new(db, run, node, (_, _, _, _, _) => { }), default);
            var result = PortfolioScenarioCalculator.Calculate(captured, [Request(.3m)]);
            Assert.Equal(expected, result.Status); Assert.Equal(2500, captured.Cash);
            Assert.Contains("國巨（2327）", result.Scenarios[0].Name);
            Assert.Equal(.2m, result.Baseline!.CashWeight); Assert.Equal(.3m, result.Scenarios[0].CashWeight);
            Assert.Equal(50, result.Scenarios[0].Quantities["2330"]);
        }
        Assert.Empty(db.PortfolioTransactions);
        Assert.Equal(2, await db.PortfolioHoldings.CountAsync());
        Assert.Single(db.PortfolioCashFlows);
    }

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); }
    }

    internal static InvestmentResearchRoutingContext Routing(IReadOnlyList<PortfolioScenarioRequest> requests) =>
        new("portfolio-risk-summary", "組合風險摘要", "比較國巨權重", new("TW", "portfolio", "deep", null, "TWD", "zh-TW"), [], [], [], "test", "high", "test", "test", "test", 1, 0, 0, 0, requests);
}
