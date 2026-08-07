using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioDiagnosisApprovalWorkflowTests
{
    [Fact]
    public async Task FullRiskRunCacheHit_SkipsPreparationAndAllMathNodes()
    {
        await using var db = CreateDb();
        var run = CreateRun();
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();

        var paused = await RunToPauseOrTerminalAsync(CreateExecutor(db, cacheHit: true), db, run);

        Assert.Equal(AgentRunStatuses.WaitingForApproval, paused.Status);
        Assert.Equal(AgentNodeStatuses.Skipped, paused.Nodes.Single(x => x.NodeKey == PortfolioRiskMathNodeKeys.PrepareInputs).Status);
        Assert.Equal(AgentNodeStatuses.Skipped, paused.Nodes.Single(x => x.NodeKey == PortfolioRiskMathNodeKeys.ExecuteCore).Status);
        Assert.DoesNotContain(paused.Nodes, x => x.NodeType == PortfolioRiskMathNodeTypes.Execute && !string.IsNullOrWhiteSpace(x.TemplateNodeKey));
        var evidence = AgentNodeJson.ParseBlackboard(paused.BlackboardJson)[AgentBlackboardKeys.PortfolioRiskEvidence]!
            .Deserialize<PortfolioRiskEvidenceSnapshot>(AgentNodeJson.SerializerOptions)!;
        Assert.Equal("Hit", evidence.CacheStatus);
        Assert.Equal(7, evidence.ReusedCapabilities.Count);
        Assert.Empty(evidence.CalculatedCapabilities);
    }

    [Fact]
    public async Task DynamicDiagnosisLoop_SupplementsMath_PausesAtFinalize_ApprovePublishes()
    {
        await using var db = CreateDb();
        var run = CreateRun();
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var executor = CreateExecutor(db);

        var paused = await RunToPauseOrTerminalAsync(executor, db, run);

        Assert.Equal(AgentRunStatuses.WaitingForApproval, paused.Status);
        var finalNode = paused.Nodes.Single(x => x.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis);
        Assert.Equal(AgentNodeStatuses.WaitingForApproval, finalNode.Status);
        Assert.Contains(paused.Nodes, x => x.TemplateNodeKey == "calculate-historical-var" && x.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(paused.Nodes, x => x.TemplateNodeKey == "calculate-expected-shortfall" && x.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(paused.Nodes, x => x.TemplateNodeKey == "calculate-portfolio-volatility" && x.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(paused.Nodes, x => x.TemplateNodeKey == "calculate-volatility-risk-contribution" && x.Status == AgentNodeStatuses.Succeeded);
        Assert.Equal(2, paused.Nodes.Count(x => x.NodeType == PortfolioDiagnosisNodeTypes.EvaluateQuality));
        var approval = await db.AgentApprovalRequests.SingleAsync(x => x.AgentRunId == run.Id);

        await new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine()).ApproveAsync(
            run.Id, approval.Id, run.UserId, false, new DecideAgentApprovalRequest(Guid.NewGuid(), "核准發布"));
        var completed = await RunToPauseOrTerminalAsync(executor, db, run);

        Assert.Equal(AgentRunStatuses.Succeeded, completed.Status);
        Assert.Equal(AgentNodeStatuses.Succeeded, completed.Nodes.Single(x => x.Id == finalNode.Id).Status);
        Assert.NotNull(completed.OutputJson);
        var runtime = AgentNodeJson.ParseBlackboard(completed.BlackboardJson)[AgentBlackboardKeys.Runtime]!;
        Assert.Equal(AgentLoopStopReasons.QualityGatePassed, runtime["stopReason"]!.GetValue<string>());
    }

    [Fact]
    public async Task DynamicDiagnosisLoop_RejectFinalize_CancelsRunWithoutPublishing()
    {
        await using var db = CreateDb();
        var run = CreateRun();
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var executor = CreateExecutor(db);
        var paused = await RunToPauseOrTerminalAsync(executor, db, run);
        var approval = await db.AgentApprovalRequests.SingleAsync(x => x.AgentRunId == run.Id);

        await new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine()).RejectAsync(
            run.Id, approval.Id, run.UserId, false, new DecideAgentApprovalRequest(Guid.NewGuid(), "證據不足，拒絕發布。"));
        await db.Entry(run).ReloadAsync();
        await db.Entry(run).Collection(x => x.Nodes).LoadAsync();

        Assert.Equal(AgentRunStatuses.Cancelled, run.Status);
        Assert.Equal(AgentNodeStatuses.Cancelled, run.Nodes.Single(x => x.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis).Status);
        Assert.Null(run.OutputJson);
        var runtime = AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.Runtime]!;
        Assert.Equal(AgentLoopStopReasons.HumanRejected, runtime["stopReason"]!.GetValue<string>());
    }

    private static AgentRun CreateRun() => new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(
        Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

    private static async Task<AgentRun> RunToPauseOrTerminalAsync(AgentRunExecutor executor, EquityLensDbContext db, AgentRun run)
    {
        for (var wake = 0; wake < 30; wake++)
        {
            await executor.ExecuteAsync(run.Id, run.UserId);
            await db.Entry(run).ReloadAsync();
            await db.Entry(run).Collection(x => x.Nodes).LoadAsync();
            if (run.Status is AgentRunStatuses.Succeeded or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled or AgentRunStatuses.WaitingForApproval)
                return run;
        }
        throw new InvalidOperationException("Portfolio diagnosis loop exceeded 30 orchestration wakes.");
    }

    private static AgentRunExecutor CreateExecutor(EquityLensDbContext db, bool cacheHit = false) => new(
        db,
        new WorkflowGraphTopologyService(),
        new AgentRunGraphValidator(),
        new AgentRunStateMachine(),
        new AgentNodeStateMachine(),
        new IAgentNodeHandler[]
        {
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.LoadContext),
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.ResolveRiskEvidence, cacheHit),
            new PortfolioFixtureHandler(PortfolioRiskMathNodeTypes.PrepareInputs),
            new PortfolioFixtureHandler(PortfolioRiskMathNodeTypes.Execute),
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.CalculateAttribution),
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.LoadRiskProfile),
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            new EvaluatePortfolioDiagnosisQualityNodeHandler(),
            new BuildPortfolioEvidencePacketNodeHandler(),
            new PortfolioFixtureHandler(PortfolioDiagnosisNodeTypes.DraftDiagnosis),
            new FinalizePortfolioDiagnosisNodeHandler()
        },
        NullLogger<AgentRunExecutor>.Instance,
        catalog: new AgentWorkflowCatalog(),
        loopController: new AgentLoopController([new PortfolioDiagnosisLoopPolicy()]));

    private sealed class PortfolioFixtureHandler(string nodeType, bool cacheHit = false) : IAgentNodeHandler
    {
        public string NodeType => nodeType;

        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
            var portfolioId = board[AgentBlackboardKeys.PortfolioId]!.GetValue<Guid>();
            switch (nodeType)
            {
                case PortfolioDiagnosisNodeTypes.LoadContext:
                    board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(
                        new PortfolioDiagnosisContext(portfolioId, "Test", "TWD", new(2025, 1, 1), new(2025, 12, 31), 2), AgentNodeJson.SerializerOptions);
                    break;
                case PortfolioDiagnosisNodeTypes.ResolveRiskEvidence:
                    if (cacheHit)
                    {
                        AddCompleteRiskRunResults(board);
                        board[AgentBlackboardKeys.PortfolioRiskEvidence] = JsonSerializer.SerializeToNode(
                            new PortfolioRiskEvidenceSnapshot([Guid.NewGuid()], PortfolioRiskEvidenceSources.PersistedRiskRun, "Hit", new(2025, 12, 31), "hash", "v1", null,
                                ["calculate-annualized-volatility", "calculate-max-drawdown", "calculate-concentration", "calculate-historical-var", "calculate-expected-shortfall", "calculate-portfolio-volatility", "calculate-volatility-risk-contribution"], [], [], false),
                            AgentNodeJson.SerializerOptions);
                        board[AgentBlackboardKeys.CoreRiskCalculationRequired] = "false";
                    }
                    else
                    {
                        board[AgentBlackboardKeys.PortfolioRiskEvidence] = JsonSerializer.SerializeToNode(
                            new PortfolioRiskEvidenceSnapshot([], PortfolioRiskEvidenceSources.CacheMiss, "Miss", null, null, null, null, [], [], [], true),
                            AgentNodeJson.SerializerOptions);
                        board[AgentBlackboardKeys.CoreRiskCalculationRequired] = "true";
                    }
                    break;
                case PortfolioRiskMathNodeTypes.PrepareInputs:
                    board[AgentBlackboardKeys.MathInputs] = new JsonObject();
                    break;
                case PortfolioRiskMathNodeTypes.Execute:
                    AddMathResults(board, context.Node);
                    break;
                case PortfolioDiagnosisNodeTypes.CalculateAttribution:
                    board[AgentBlackboardKeys.PerformanceAttribution] = JsonSerializer.SerializeToNode(
                        new PortfolioPerformanceAttribution(.1m, .08m, .02m, 2, 2, [], [], "complete"), AgentNodeJson.SerializerOptions);
                    break;
                case PortfolioDiagnosisNodeTypes.LoadRiskProfile:
                    board[AgentBlackboardKeys.RiskProfile] = JsonSerializer.SerializeToNode(
                        new PortfolioRiskProfileSnapshot(true, "ready", new(2025, 12, 31), 180, 0), AgentNodeJson.SerializerOptions);
                    break;
                case PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses:
                    board[AgentBlackboardKeys.RiskAnalysisPriorities] = JsonSerializer.SerializeToNode(new List<RiskAnalysisPriority>(), AgentNodeJson.SerializerOptions);
                    break;
                case PortfolioDiagnosisNodeTypes.DraftDiagnosis:
                    board[AgentBlackboardKeys.PortfolioDiagnosisDraft] = JsonSerializer.SerializeToNode(
                        new PortfolioDiagnosisOutput("ok", .1m, .08m, .02m, [], [], [], "complete",
                            new(.2m, -.1m, -.03m, -.04m, .18m, .5m, .6m, 1m), null), AgentNodeJson.SerializerOptions);
                    break;
            }
            context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
            return Task.CompletedTask;
        }

        private static void AddMathResults(JsonObject board, AgentRunNode node)
        {
            var results = board[AgentBlackboardKeys.MathResults] as JsonArray ?? new JsonArray();
            board[AgentBlackboardKeys.MathResults] = results;
            var operations = string.IsNullOrWhiteSpace(node.TemplateNodeKey)
                ? new[] { "calculate-portfolio-return", "calculate-concentration", "calculate-annualized-volatility", "calculate-max-drawdown" }
                : new[] { node.TemplateNodeKey };
            foreach (var operation in operations)
            {
                JsonNode? value = operation switch
                {
                    "calculate-concentration" => new JsonObject { ["hhi"] = .5m, ["largestWeight"] = .6m },
                    "calculate-volatility-risk-contribution" => new JsonObject { ["componentRiskShare"] = 1m },
                    "calculate-historical-var" => JsonValue.Create(-.03m),
                    "calculate-expected-shortfall" => JsonValue.Create(-.04m),
                    "calculate-portfolio-volatility" => JsonValue.Create(.18m),
                    "calculate-max-drawdown" => JsonValue.Create(-.1m),
                    _ => JsonValue.Create(.2m)
                };
                results.Add(new JsonObject { ["operation"] = operation, ["value"] = value });
            }
        }

        private static void AddCompleteRiskRunResults(JsonObject board)
        {
            board[AgentBlackboardKeys.MathResults] = new JsonArray
            {
                new JsonObject { ["operation"] = "calculate-annualized-volatility", ["value"] = .2m },
                new JsonObject { ["operation"] = "calculate-max-drawdown", ["value"] = -.1m },
                new JsonObject { ["operation"] = "calculate-concentration", ["value"] = new JsonObject { ["hhi"] = .5m, ["largestWeight"] = .6m } },
                new JsonObject { ["operation"] = "calculate-historical-var", ["value"] = -.03m },
                new JsonObject { ["operation"] = "calculate-expected-shortfall", ["value"] = -.04m },
                new JsonObject { ["operation"] = "calculate-portfolio-volatility", ["value"] = .18m },
                new JsonObject { ["operation"] = "calculate-volatility-risk-contribution", ["value"] = new JsonObject { ["componentRiskShare"] = 1m } }
            };
        }
    }

    private static EquityLensDbContext CreateDb() => new TestDb(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunWakeOutbox>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentApprovalRequest>().Property(x => x.Id).ValueGeneratedNever();
        }
    }
}
