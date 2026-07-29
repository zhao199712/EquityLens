using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioDiagnosisApprovalWorkflowTests
{
    [Fact]
    public async Task PortfolioDiagnosis_PausesAtApprovalGate_ApproveCompletesRun()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateRun(userId);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var queue = new RecordingAgentRunQueue();
        var executor = CreateExecutor(db);
        var service = CreateService(db, queue);

        var pausedStatus = await RunToPauseOrTerminalAsync(executor, db, run.Id, userId);

        Assert.Equal(AgentRunStatuses.WaitingForFeedback, pausedStatus);
        var paused = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == run.Id);
        Assert.Equal(AgentNodeStatuses.WaitingForFeedback, paused.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.ApproveDiagnosis).Status);
        Assert.Equal(AgentNodeStatuses.Succeeded, paused.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.DraftDiagnosis).Status);
        var request = AgentNodeJson.ParseBlackboard(paused.BlackboardJson)[AgentBlackboardKeys.ApprovalRequest] as JsonObject;
        Assert.NotNull(request);
        Assert.Contains("投組診斷", request!["prompt"]!.GetValue<string>(), StringComparison.Ordinal);

        await service.DecideApprovalAsync(run.Id, userId, HumanApprovalDecisions.Approved, null);
        var finalStatus = await RunToPauseOrTerminalAsync(executor, db, run.Id, userId);

        Assert.Equal(AgentRunStatuses.Succeeded, finalStatus);
        var completed = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == run.Id);
        Assert.Equal(AgentNodeStatuses.Succeeded, completed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.ApproveDiagnosis).Status);
        Assert.Equal(AgentNodeStatuses.Succeeded, completed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.FinalizeDiagnosis).Status);
        Assert.Equal(AgentNodeStatuses.Skipped, completed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis).Status);
        Assert.Equal(HumanApprovalDecisions.Approved, AgentNodeJson.ParseBlackboard(completed.BlackboardJson)[AgentBlackboardKeys.HumanApproval]!["decision"]!.GetValue<string>());
    }

    [Fact]
    public async Task PortfolioDiagnosis_PausesAtApprovalGate_RejectFailsRun()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateRun(userId);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var executor = CreateExecutor(db);
        var service = CreateService(db, new RecordingAgentRunQueue());

        var pausedStatus = await RunToPauseOrTerminalAsync(executor, db, run.Id, userId);
        Assert.Equal(AgentRunStatuses.WaitingForFeedback, pausedStatus);

        await service.DecideApprovalAsync(run.Id, userId, HumanApprovalDecisions.Rejected, "證據不足，拒絕發布。");
        var finalStatus = await RunToPauseOrTerminalAsync(executor, db, run.Id, userId);

        Assert.Equal(AgentRunStatuses.Failed, finalStatus);
        var failed = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == run.Id);
        Assert.Contains("人工拒絕此投組診斷", failed.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("證據不足", failed.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(AgentNodeStatuses.Succeeded, failed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.ApproveDiagnosis).Status);
        Assert.Equal(AgentNodeStatuses.Skipped, failed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.FinalizeDiagnosis).Status);
        Assert.Equal(AgentNodeStatuses.Failed, failed.Nodes.Single(x => x.NodeKey == PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis).Status);
        Assert.Equal(HumanApprovalDecisions.Rejected, AgentNodeJson.ParseBlackboard(failed.BlackboardJson)[AgentBlackboardKeys.HumanApproval]!["decision"]!.GetValue<string>());
        Assert.Contains("rejected", failed.OutputJson, StringComparison.Ordinal);
    }

    private static AgentRun CreateRun(Guid userId) =>
        new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(userId, Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));

    private static async Task<string> RunToPauseOrTerminalAsync(AgentRunExecutor executor, EquityLensDbContext db, Guid runId, Guid userId)
    {
        for (var wake = 0; wake < 100; wake++)
        {
            await executor.ExecuteAsync(runId, userId);
            var status = await db.AgentRuns.Where(x => x.Id == runId).Select(x => x.Status).SingleAsync();
            if (status is AgentRunStatuses.Succeeded or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled or AgentRunStatuses.WaitingForFeedback)
                return status;
        }
        throw new InvalidOperationException("Approval workflow test exceeded 100 orchestration wakes.");
    }

    private static EquityLensDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestEquityLensDbContext(options);
    }

    private static AgentRunExecutor CreateExecutor(EquityLensDbContext db) => new(
        db,
        new AgentWorkflowPlanner(),
        new AgentRunGraphValidator(),
        new AgentRunStateMachine(),
        new AgentNodeStateMachine(),
        new IAgentNodeHandler[]
        {
            new StubHandler(PortfolioDiagnosisNodeTypes.LoadContext),
            new StubHandler(PortfolioRiskMathNodeTypes.PrepareInputs),
            new StubHandler(PortfolioRiskMathNodeTypes.Execute),
            new StubHandler(PortfolioDiagnosisNodeTypes.CalculateAttribution),
            new StubHandler(PortfolioDiagnosisNodeTypes.LoadRiskProfile),
            new StubHandler(PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            new StubHandler(PortfolioDiagnosisNodeTypes.BuildEvidencePacket),
            new StubHandler(PortfolioDiagnosisNodeTypes.DraftDiagnosis),
            new WaitForHumanApprovalNodeHandler(),
            new StubHandler(PortfolioDiagnosisNodeTypes.FinalizeDiagnosis),
            new FinalizeRejectedPortfolioDiagnosisNodeHandler()
        },
        NullLogger<AgentRunExecutor>.Instance);

    private static AgentRunService CreateService(EquityLensDbContext db, IAgentRunQueue queue) => new(
        db,
        Array.Empty<IAgentWorkflowDefinitionProvider>(),
        new AgentRunStateMachine(),
        new AgentNodeStateMachine(),
        queue);

    private sealed class StubHandler(string nodeType) : IAgentNodeHandler
    {
        public string NodeType => nodeType;

        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAgentRunQueue : IAgentRunQueue
    {
        public List<AgentRunQueueMessage> Messages { get; } = [];

        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task<AgentRunQueueItem?> ReadStalePendingAsync(string consumerName, TimeSpan minIdleTime, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestEquityLensDbContext : EquityLensDbContext
    {
        public TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentFeedback>().Property(x => x.Id).ValueGeneratedNever();
        }
    }
}
