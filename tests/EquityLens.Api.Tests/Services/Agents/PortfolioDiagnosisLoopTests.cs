using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioDiagnosisLoopTests
{
    [Fact]
    public async Task QualityGate_MultiAssetWithEnoughHistory_RequestsFourMissingAnalyses()
    {
        await using var db = CreateDb();
        var run = CreateRun(holdingCount: 2, commonTradingDays: 180, BaselineResults());
        var node = QualityNode(run, 0);

        await new EvaluatePortfolioDiagnosisQualityNodeHandler()
            .ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var result = Quality(run);
        Assert.Equal(PortfolioDiagnosisQualityStatuses.NeedsAnalysis, result.Status);
        Assert.Equal([
            "calculate-historical-var",
            "calculate-expected-shortfall",
            "calculate-portfolio-volatility",
            "calculate-volatility-risk-contribution"
        ], result.RequiredCapabilities);
        Assert.All(result.Gaps, gap => Assert.True(gap.Resolvable));
    }

    [Fact]
    public async Task QualityGate_SingleAssetWithEnoughHistory_RequestsOnlyTailRiskAnalyses()
    {
        await using var db = CreateDb();
        var run = CreateRun(holdingCount: 1, commonTradingDays: 180, BaselineResults());

        await new EvaluatePortfolioDiagnosisQualityNodeHandler()
            .ExecuteAsync(new AgentNodeExecutionContext(db, run, QualityNode(run, 0), (_, _, _, _, _) => { }));

        Assert.Equal(["calculate-historical-var", "calculate-expected-shortfall"], Quality(run).RequiredCapabilities);
    }

    [Fact]
    public async Task QualityGate_InsufficientHistory_IsLimitedWithoutSchedulingAdvancedMath()
    {
        await using var db = CreateDb();
        var run = CreateRun(holdingCount: 2, commonTradingDays: 80, BaselineResults());

        await new EvaluatePortfolioDiagnosisQualityNodeHandler()
            .ExecuteAsync(new AgentNodeExecutionContext(db, run, QualityNode(run, 0), (_, _, _, _, _) => { }));

        var result = Quality(run);
        Assert.Equal(PortfolioDiagnosisQualityStatuses.Limited, result.Status);
        Assert.Empty(result.RequiredCapabilities);
        Assert.Contains(result.Gaps, x => x.Code == "INSUFFICIENT_HISTORY" && !x.Resolvable);
    }

    [Fact]
    public void LoopPolicy_NeedsAnalysisThenPass_ChoosesSupplementAndFinalizationBranches()
    {
        var run = CreateRun(holdingCount: 2, commonTradingDays: 180, BaselineResults());
        var completedGate = QualityNode(run, 0);
        completedGate.Status = AgentNodeStatuses.Succeeded;
        completedGate.CompletedAtUtc = DateTime.UtcNow;
        run.Nodes.Add(completedGate);
        SetQuality(run, PortfolioDiagnosisQualityStatuses.NeedsAnalysis, 0,
            [new("HISTORICAL_VAR_MISSING", true, "calculate-historical-var", "missing")],
            ["calculate-historical-var"], ["calculate-annualized-volatility"]);
        var policy = new PortfolioDiagnosisLoopPolicy();

        var supplement = policy.Evaluate(run);
        SetQuality(run, PortfolioDiagnosisQualityStatuses.Pass, 1, [], [],
            ["calculate-annualized-volatility", "calculate-historical-var"]);
        var finalize = policy.Evaluate(run);

        Assert.Equal(AgentLoopActions.RunRiskAnalyses, supplement.Action);
        Assert.Equal(1, supplement.Iteration);
        Assert.Equal(AgentLoopActions.FinalizeDiagnosis, finalize.Action);
        Assert.Equal(AgentLoopStopReasons.QualityGatePassed, finalize.ReasonCode);
    }

    private static AgentRun CreateRun(int holdingCount, int commonTradingDays, JsonArray results)
    {
        var portfolioId = Guid.NewGuid();
        var board = AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(
            portfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(
            new PortfolioDiagnosisContext(portfolioId, "Test", "TWD", new(2025, 1, 1), new(2025, 12, 31), holdingCount), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.PerformanceAttribution] = JsonSerializer.SerializeToNode(
            new PortfolioPerformanceAttribution(.1m, .08m, .02m, holdingCount, holdingCount, [], [], "complete"), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.RiskProfile] = JsonSerializer.SerializeToNode(
            new PortfolioRiskProfileSnapshot(true, "ready", new(2025, 12, 31), commonTradingDays, 0), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.MathResults] = results;
        return new AgentRun
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis,
            AgentType = AgentTypes.Portfolio, BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions), Nodes = []
        };
    }

    private static JsonArray BaselineResults() =>
    [
        new JsonObject { ["operation"] = "calculate-annualized-volatility", ["value"] = .2m },
        new JsonObject { ["operation"] = "calculate-max-drawdown", ["value"] = -.1m },
        new JsonObject { ["operation"] = "calculate-concentration", ["value"] = new JsonObject { ["hhi"] = .5m, ["largestWeight"] = .6m } }
    ];

    private static AgentRunNode QualityNode(AgentRun run, int iteration) => new()
    {
        Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = $"quality:{iteration}",
        NodeType = PortfolioDiagnosisNodeTypes.EvaluateQuality, Iteration = iteration
    };

    private static PortfolioDiagnosisQualityResult Quality(AgentRun run) =>
        AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.PortfolioDiagnosisQuality]!
            .Deserialize<PortfolioDiagnosisQualityResult>(AgentNodeJson.SerializerOptions)!;

    private static void SetQuality(AgentRun run, string status, int iteration, IReadOnlyList<PortfolioDiagnosisGap> gaps,
        IReadOnlyList<string> required, IReadOnlyList<string> completed)
    {
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.PortfolioDiagnosisQuality] = JsonSerializer.SerializeToNode(
            new PortfolioDiagnosisQualityResult(status, iteration, gaps, required, completed,
                new(.2m, -.1m, null, null, null, .5m, .6m, null)), AgentNodeJson.SerializerOptions);
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
    }

    private static EquityLensDbContext CreateDb() => new TestDb(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }
}
