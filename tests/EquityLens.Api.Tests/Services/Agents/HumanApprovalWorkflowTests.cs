using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class HumanApprovalWorkflowTests
{
    private const string GateKey = "waitForHumanApproval";
    private const string PublishKey = "publishAnswer";
    private const string RejectKey = "finalizeRejected";
    private const string StampNodeType = "TestStampNode";

    [Fact]
    public async Task Executor_PausesAtApprovalGate_AndReleasesLeaseWithoutWake()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var executor = CreateExecutor(db);

        await executor.ExecuteAsync(run.Id, userId);

        var reloaded = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == run.Id);
        Assert.Equal(AgentRunStatuses.WaitingForFeedback, reloaded.Status);
        Assert.Null(reloaded.LeaseOwner);
        Assert.Null(reloaded.LeaseExpiresAtUtc);
        var gate = reloaded.Nodes.Single(x => x.NodeKey == GateKey);
        Assert.Equal(AgentNodeStatuses.WaitingForFeedback, gate.Status);
        Assert.All(reloaded.Nodes.Where(x => x.NodeKey != GateKey), n => Assert.Equal(AgentNodeStatuses.Pending, n.Status));
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
        var request = AgentNodeJson.ParseBlackboard(reloaded.BlackboardJson)[AgentBlackboardKeys.ApprovalRequest] as JsonObject;
        Assert.NotNull(request);
        Assert.Equal("ApproveReject", request!["approvalType"]!.GetValue<string>());
        Assert.Equal("請審核診斷結果", request["prompt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Executor_SkipsPausedRun_OnStrayWake()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        run.Status = AgentRunStatuses.WaitingForFeedback;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var executor = CreateExecutor(db);

        await executor.ExecuteAsync(run.Id, userId);

        Assert.Equal(AgentRunStatuses.WaitingForFeedback, (await db.AgentRuns.SingleAsync(x => x.Id == run.Id)).Status);
    }

    [Theory]
    [InlineData(HumanApprovalDecisions.Approved, PublishKey, RejectKey)]
    [InlineData(HumanApprovalDecisions.Rejected, RejectKey, PublishKey)]
    public async Task DecideApproval_ResumesRun_AndConditionalEdgesBranch(string decision, string expectedRunKey, string expectedSkippedKey)
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var queue = new RecordingAgentRunQueue();
        var executor = CreateExecutor(db);
        var service = CreateService(db, queue);
        await executor.ExecuteAsync(run.Id, userId);
        Assert.Equal(AgentRunStatuses.WaitingForFeedback, (await db.AgentRuns.SingleAsync(x => x.Id == run.Id)).Status);

        var comment = decision == HumanApprovalDecisions.Rejected ? "證據不足，拒絕發布。" : null;
        var summary = await service.DecideApprovalAsync(run.Id, userId, decision, comment);

        Assert.Equal(AgentRunStatuses.Running, summary.Status);
        Assert.Single(queue.Messages);
        await executor.ExecuteAsync(run.Id, userId);

        var reloaded = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == run.Id);
        Assert.Equal(AgentRunStatuses.Succeeded, reloaded.Status);
        var gate = reloaded.Nodes.Single(x => x.NodeKey == GateKey);
        Assert.Equal(AgentNodeStatuses.Succeeded, gate.Status);
        var board = AgentNodeJson.ParseBlackboard(reloaded.BlackboardJson);
        Assert.Equal(decision, board[AgentBlackboardKeys.HumanApproval]!["decision"]!.GetValue<string>());
        Assert.Equal(decision, JsonNode.Parse(gate.OutputJson!)!["decision"]!.GetValue<string>());
        Assert.Equal(AgentNodeStatuses.Succeeded, reloaded.Nodes.Single(x => x.NodeKey == expectedRunKey).Status);
        Assert.Equal(AgentNodeStatuses.Skipped, reloaded.Nodes.Single(x => x.NodeKey == expectedSkippedKey).Status);
    }

    [Fact]
    public async Task DecideApproval_NonWaitingRun_Throws()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        run.Status = AgentRunStatuses.Running;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingAgentRunQueue());

        var exception = await Assert.ThrowsAsync<AgentApprovalException>(() =>
            service.DecideApprovalAsync(run.Id, userId, HumanApprovalDecisions.Approved, null));

        Assert.Equal("agent_run_not_waiting", exception.Code);
    }

    [Fact]
    public async Task DecideApproval_RejectWithoutComment_Throws()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        run.Status = AgentRunStatuses.WaitingForFeedback;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingAgentRunQueue());

        var exception = await Assert.ThrowsAsync<AgentApprovalException>(() =>
            service.DecideApprovalAsync(run.Id, userId, HumanApprovalDecisions.Rejected, "   "));

        Assert.Equal("approval_comment_required", exception.Code);
    }

    [Fact]
    public async Task DecideApproval_InvalidDecision_Throws()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        run.Status = AgentRunStatuses.WaitingForFeedback;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingAgentRunQueue());

        var exception = await Assert.ThrowsAsync<AgentApprovalException>(() =>
            service.DecideApprovalAsync(run.Id, userId, "Maybe", null));

        Assert.Equal("approval_decision_invalid", exception.Code);
    }

    [Fact]
    public async Task DecideApproval_OtherUser_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        run.Status = AgentRunStatuses.WaitingForFeedback;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingAgentRunQueue());

        var exception = await Assert.ThrowsAsync<AgentApprovalException>(() =>
            service.DecideApprovalAsync(run.Id, Guid.NewGuid(), HumanApprovalDecisions.Approved, null));

        Assert.Equal("agent_run_not_found", exception.Code);
    }

    [Fact]
    public async Task Handler_FirstRunRequestsApproval_SecondRunAppliesDecision()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var run = CreateApprovalRun(userId);
        var gate = run.Nodes.Single(x => x.NodeKey == GateKey);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new WaitForHumanApprovalNodeHandler();

        var firstContext = new AgentNodeExecutionContext(db, run, gate, (_, _, _, _, _) => { });
        await handler.ExecuteAsync(firstContext);

        Assert.True(firstContext.AwaitingApproval);
        Assert.NotNull(AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.ApprovalRequest]);

        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        blackboard[AgentBlackboardKeys.ApprovalDecision] = JsonSerializer.SerializeToNode(
            new { decision = HumanApprovalDecisions.Approved, comment = (string?)null, reviewerId = userId, decidedAtUtc = DateTime.UtcNow },
            AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        var secondContext = new AgentNodeExecutionContext(db, run, gate, (_, _, _, _, _) => { });
        await handler.ExecuteAsync(secondContext);

        Assert.False(secondContext.AwaitingApproval);
        Assert.Equal(HumanApprovalDecisions.Approved, JsonNode.Parse(gate.OutputJson!)!["decision"]!.GetValue<string>());
        Assert.Equal(HumanApprovalDecisions.Approved, AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.HumanApproval]!["decision"]!.GetValue<string>());
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
        new IAgentNodeHandler[] { new WaitForHumanApprovalNodeHandler(), new StampNodeHandler() },
        NullLogger<AgentRunExecutor>.Instance);

    private static AgentRunService CreateService(EquityLensDbContext db, IAgentRunQueue queue) => new(
        db,
        Array.Empty<IAgentWorkflowDefinitionProvider>(),
        new AgentRunStateMachine(),
        new AgentNodeStateMachine(),
        queue);

    private static AgentRun CreateApprovalRun(Guid userId)
    {
        var runId = Guid.NewGuid();
        var definition = new JsonObject
        {
            ["workflowType"] = "HumanApprovalTest",
            ["version"] = 1,
            ["nodes"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = GateKey,
                    ["type"] = HumanApprovalNodeTypes.WaitForHumanApproval,
                    ["required"] = true,
                    ["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = 120, ["maxRetryCount"] = 0 },
                    ["config"] = new JsonObject { ["approvalType"] = "ApproveReject", ["prompt"] = "請審核診斷結果", ["subjectKey"] = "draft" }
                },
                new JsonObject
                {
                    ["id"] = PublishKey,
                    ["type"] = StampNodeType,
                    ["required"] = true,
                    ["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = 120, ["maxRetryCount"] = 0 },
                    ["condition"] = new JsonObject { ["path"] = "humanApproval.decision", ["equals"] = HumanApprovalDecisions.Approved }
                },
                new JsonObject
                {
                    ["id"] = RejectKey,
                    ["type"] = StampNodeType,
                    ["required"] = true,
                    ["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = 120, ["maxRetryCount"] = 0 },
                    ["condition"] = new JsonObject { ["path"] = "humanApproval.decision", ["equals"] = HumanApprovalDecisions.Rejected }
                }),
            ["edges"] = new JsonArray(
                new JsonObject { ["from"] = GateKey, ["to"] = PublishKey },
                new JsonObject { ["from"] = GateKey, ["to"] = RejectKey })
        };
        var blackboard = new JsonObject
        {
            ["draft"] = "診斷草稿內容",
            ["schemaVersion"] = 1,
            ["blackboardVersion"] = 0
        };
        var run = new AgentRun
        {
            Id = runId,
            UserId = userId,
            WorkflowType = "HumanApprovalTest",
            AgentType = AgentTypes.Analysis,
            Status = AgentRunStatuses.Pending,
            InputJson = "{}",
            BlackboardJson = blackboard.ToJsonString(),
            WorkflowDefinitionJson = definition.ToJsonString()
        };
        run.Nodes = new List<AgentRunNode>
        {
            new() { Id = Guid.NewGuid(), AgentRunId = runId, NodeKey = GateKey, NodeType = HumanApprovalNodeTypes.WaitForHumanApproval, Status = AgentNodeStatuses.Pending, TemplateNodeKey = GateKey },
            new() { Id = Guid.NewGuid(), AgentRunId = runId, NodeKey = PublishKey, NodeType = StampNodeType, Status = AgentNodeStatuses.Pending, TemplateNodeKey = PublishKey },
            new() { Id = Guid.NewGuid(), AgentRunId = runId, NodeKey = RejectKey, NodeType = StampNodeType, Status = AgentNodeStatuses.Pending, TemplateNodeKey = RejectKey }
        };
        return run;
    }

    private sealed class StampNodeHandler : IAgentNodeHandler
    {
        public string NodeType => StampNodeType;

        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.Node.OutputJson = "{\"stamped\":true}";
            return Task.CompletedTask;
        }
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
