using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioDiagnosisNodeHandlersTests
{
    [Fact]
    public async Task DraftHandler_SurfacesRiskMetricsFromMathResults()
    {
        await using var db = CreateDb();
        var run = CreateRun(MathResults());
        var node = DraftNode(run.Id);

        await new DraftPortfolioDiagnosisNodeHandler(new FakeNarrative("解讀文字")).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var output = JsonNode.Parse(node.OutputJson!)!;
        var metrics = output["riskMetrics"]!;
        Assert.Equal(0.3871m, metrics["annualizedVolatility"]!.GetValue<decimal>());
        Assert.Equal(-0.0858m, metrics["maxDrawdown"]!.GetValue<decimal>());
        Assert.Equal(-0.0729m, metrics["historicalVaR"]!.GetValue<decimal>());
        Assert.Equal(-0.0729m, metrics["expectedShortfall"]!.GetValue<decimal>());
        Assert.Equal(0.0247m, metrics["portfolioVolatility"]!.GetValue<decimal>());
        Assert.Equal(1m, metrics["concentrationHhi"]!.GetValue<decimal>());
        Assert.Equal(1m, metrics["largestWeight"]!.GetValue<decimal>());
        Assert.Equal(0.999m, metrics["volatilityRiskShare"]!.GetValue<decimal>());
        var summary = output["summary"]!.GetValue<string>();
        Assert.Contains("風險概況", summary, StringComparison.Ordinal);
        Assert.Contains("最大回撤", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftHandler_DegradesWhenMathResultsAreEmpty()
    {
        await using var db = CreateDb();
        var run = CreateRun(new JsonArray());
        var node = DraftNode(run.Id);

        await new DraftPortfolioDiagnosisNodeHandler(new FakeNarrative("解讀文字")).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var output = JsonNode.Parse(node.OutputJson!)!;
        var metrics = output["riskMetrics"]!;
        Assert.True(metrics["annualizedVolatility"] is null);
        Assert.True(metrics["maxDrawdown"] is null);
        Assert.True(metrics["concentrationHhi"] is null);
        Assert.DoesNotContain("風險概況", output["summary"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftHandler_WritesNarrativeInterpretation()
    {
        await using var db = CreateDb();
        var run = CreateRun(MathResults());
        var node = DraftNode(run.Id);

        await new DraftPortfolioDiagnosisNodeHandler(new FakeNarrative("**風險集中**於單一持倉。")).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var output = JsonNode.Parse(node.OutputJson!)!;
        Assert.Equal("**風險集中**於單一持倉。", output["interpretation"]!.GetValue<string>());
    }

    [Fact]
    public async Task DraftHandler_KeepsReportWhenNarrativeAgentFails()
    {
        await using var db = CreateDb();
        var run = CreateRun(MathResults());
        var node = DraftNode(run.Id);

        await new DraftPortfolioDiagnosisNodeHandler(new ThrowingNarrative()).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var output = JsonNode.Parse(node.OutputJson!)!;
        Assert.True(output["interpretation"] is null);
        Assert.Contains("風險概況", output["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(0.3871m, output["riskMetrics"]!["annualizedVolatility"]!.GetValue<decimal>());
    }

    [Fact]
    public void MetricsReader_IgnoresUnknownAndScalarShapes()
    {
        var mathResults = new JsonArray
        {
            new JsonObject { ["operation"] = "calculate-annualized-volatility", ["value"] = 0.25m },
            new JsonObject { ["operation"] = "calculate-concentration", ["value"] = 0.5m },
            new JsonObject { ["operation"] = "calculate-unknown", ["value"] = 99m }
        };

        var metrics = PortfolioRiskMetricsReader.FromMathResults(mathResults);

        Assert.Equal(0.25m, metrics.AnnualizedVolatility);
        Assert.True(metrics.ConcentrationHhi is null);
        Assert.True(metrics.MaxDrawdown is null);
    }

    private static AgentRun CreateRun(JsonArray mathResults)
    {
        var portfolioId = Guid.NewGuid();
        var board = AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var context = new PortfolioDiagnosisContext(portfolioId, "測試投組", "TWD", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 1);
        board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(context, AgentNodeJson.SerializerOptions);
        var attribution = new PortfolioPerformanceAttribution(-0.0486m, -0.0147m, -0.0339m, 1, 1,
            [new AttributionItem("holding-1", "2330 台積電", "半導體業", 1m, -0.0084m, -0.0084m)], [], "complete");
        board[AgentBlackboardKeys.PerformanceAttribution] = JsonSerializer.SerializeToNode(attribution, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.RiskAnalysisPriorities] = JsonSerializer.SerializeToNode(
            new List<RiskAnalysisPriority> { new("risk-backtest", 1, "執行 VaR 回測", "驗證覆蓋率。") }, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.MathResults] = mathResults;
        return new AgentRun { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis, AgentType = AgentTypes.Portfolio, BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions) };
    }

    private static AgentRunNode DraftNode(Guid runId) => new() { Id = Guid.NewGuid(), AgentRunId = runId, NodeKey = PortfolioDiagnosisNodeKeys.DraftDiagnosis, NodeType = PortfolioDiagnosisNodeTypes.DraftDiagnosis };

    private static JsonArray MathResults() => new()
    {
        new JsonObject { ["operation"] = "calculate-annualized-volatility", ["value"] = 0.3871m },
        new JsonObject { ["operation"] = "calculate-max-drawdown", ["value"] = -0.0858m },
        new JsonObject { ["operation"] = "calculate-historical-var", ["value"] = -0.0729m },
        new JsonObject { ["operation"] = "calculate-expected-shortfall", ["value"] = -0.0729m },
        new JsonObject { ["operation"] = "calculate-portfolio-volatility", ["value"] = 0.0247m },
        new JsonObject { ["operation"] = "calculate-concentration", ["value"] = new JsonObject { ["hhi"] = 1m, ["largestWeight"] = 1m } },
        new JsonObject { ["operation"] = "calculate-volatility-risk-contribution", ["value"] = new JsonObject { ["componentRiskShare"] = 0.999m, ["marginalVolatility"] = 0.39m, ["componentVolatility"] = 0.39m } }
    };

    private sealed class FakeNarrative(string text) : IPortfolioDiagnosisNarrativeAgent
    {
        public Task<string> GenerateAsync(PortfolioDiagnosisNarrativeInput input, CancellationToken cancellationToken = default) => Task.FromResult(text);
    }

    private sealed class ThrowingNarrative : IPortfolioDiagnosisNarrativeAgent
    {
        public Task<string> GenerateAsync(PortfolioDiagnosisNarrativeInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("LLM unavailable.");
    }

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); }
    }
}
