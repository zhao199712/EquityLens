using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentApprovalServiceTests
{
    [Fact]
    public async Task NodeApprovalPolicy_AdminOverrideWinsAndCanReturnToCatalogDefault()
    {
        await using var db = CreateDb();
        var catalog = new AgentWorkflowCatalog();
        var service = new AgentWorkflowAdminService(db, catalog);
        var nodeType = CriticReviewNodeTypes.LoadResearchRun;

        await service.UpdateNodeAsync(nodeType, new(true, null, null, 120, 0, null, true));
        var overridden = await service.GetApprovalPoliciesAsync([nodeType]);
        var adminView = Assert.Single(await service.ListNodesAsync(), x => x.NodeType == nodeType);

        Assert.True(overridden[nodeType]);
        Assert.True(adminView.RequiresHumanApproval);
        Assert.Equal("AdminOverride", adminView.ApprovalPolicySource);

        await service.UpdateNodeAsync(nodeType, new(true, null, null, 120, 0, null, null));
        var inherited = await service.GetApprovalPoliciesAsync([nodeType]);

        Assert.False(inherited[nodeType]);
    }

    [Fact]
    public async Task Executor_ApprovalGate_PausesBeforeHandlerAndResumesOnceApproved()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var node = new AgentRunNode
        {
            Id = Guid.NewGuid(), NodeKey = "load", TemplateNodeKey = "load",
            NodeType = CriticReviewNodeTypes.LoadResearchRun, Status = AgentNodeStatuses.Pending
        };
        var run = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.CriticReview,
            AgentType = AgentTypes.Critic, Status = AgentRunStatuses.Pending,
            BlackboardJson = """{"researchRunId":"00000000-0000-0000-0000-000000000001"}""",
            WorkflowDefinitionJson = """{"version":1,"nodes":[{"id":"load","type":"LoadResearchRun","required":true,"executionPolicy":{"timeoutSeconds":30,"maxRetryCount":0},"approvalPolicy":{"requiresHumanApproval":true,"sideEffectLevel":"ExternalWrite","reason":"需要人工批准"}}],"edges":[]}""",
            Nodes = [node]
        };
        node.AgentRunId = run.Id;
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new CountingNodeHandler();
        var executor = new AgentRunExecutor(
            db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(), [handler],
            NullLogger<AgentRunExecutor>.Instance, new AgentWorkflowCatalog());

        await executor.ExecuteAsync(run.Id, userId);

        Assert.Equal(AgentRunStatuses.WaitingForApproval, run.Status);
        Assert.Equal(AgentNodeStatuses.WaitingForApproval, node.Status);
        Assert.Equal(0, handler.CallCount);
        var approval = Assert.Single(db.AgentApprovalRequests);

        await CreateService(db).ApproveAsync(run.Id, approval.Id, userId, false, new(Guid.NewGuid()));
        await executor.ExecuteAsync(run.Id, userId);

        Assert.Equal(AgentRunStatuses.Succeeded, run.Status);
        Assert.Equal(AgentNodeStatuses.Succeeded, node.Status);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(approval.ConsumedAtUtc);
    }

    [Fact]
    public async Task ApproveAsync_OwnerApproves_WakesRunAndIsIdempotent()
    {
        await using var db = CreateDb();
        var artifact = SeedWaitingRun(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var requestId = Guid.NewGuid();

        var first = await service.ApproveAsync(artifact.Run.Id, artifact.Approval.Id, artifact.Run.UserId, false, new(requestId, "確認執行"));
        var replay = await service.ApproveAsync(artifact.Run.Id, artifact.Approval.Id, artifact.Run.UserId, false, new(requestId, "確認執行"));

        Assert.NotNull(first);
        Assert.Equal(AgentApprovalStatuses.Approved, first.Status);
        Assert.Equal(first, replay);
        Assert.Equal(AgentRunStatuses.Running, artifact.Run.Status);
        Assert.Equal(AgentNodeStatuses.Pending, artifact.Node.Status);
        Assert.Single(db.AgentRunWakeOutbox);
        Assert.Contains(db.AgentRunEvents, x => x.EventType == AgentEventTypes.ApprovalApproved);
    }

    [Fact]
    public async Task ApproveAsync_DifferentOwner_ReturnsNotFoundWithoutMutation()
    {
        await using var db = CreateDb();
        var artifact = SeedWaitingRun(db);
        await db.SaveChangesAsync();

        var result = await CreateService(db).ApproveAsync(
            artifact.Run.Id, artifact.Approval.Id, Guid.NewGuid(), false, new(Guid.NewGuid()));

        Assert.Null(result);
        Assert.Equal(AgentApprovalStatuses.Pending, artifact.Approval.Status);
        Assert.Empty(db.AgentRunWakeOutbox);
    }

    [Fact]
    public async Task RejectAsync_AdminCancelsRunAndAllOutstandingNodes()
    {
        await using var db = CreateDb();
        var artifact = SeedWaitingRun(db);
        var downstream = new AgentRunNode
        {
            Id = Guid.NewGuid(), AgentRunId = artifact.Run.Id, NodeKey = "next", TemplateNodeKey = "next",
            NodeType = CriticReviewNodeTypes.BuildEvidencePacket, Status = AgentNodeStatuses.Pending
        };
        artifact.Run.Nodes.Add(downstream);
        await db.SaveChangesAsync();

        var result = await CreateService(db).RejectAsync(
            artifact.Run.Id, artifact.Approval.Id, Guid.NewGuid(), true, new(Guid.NewGuid(), "不允許此外部動作"));

        Assert.NotNull(result);
        Assert.Equal(AgentApprovalStatuses.Rejected, result.Status);
        Assert.Equal(AgentRunStatuses.Cancelled, artifact.Run.Status);
        Assert.All(artifact.Run.Nodes, x => Assert.Equal(AgentNodeStatuses.Cancelled, x.Status));
        Assert.Empty(db.AgentRunWakeOutbox);
        Assert.Contains(db.AgentRunEvents, x => x.EventType == AgentEventTypes.ApprovalRejected);
    }

    [Fact]
    public async Task RejectAsync_WithoutReason_IsRejected()
    {
        await using var db = CreateDb();
        var artifact = SeedWaitingRun(db);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AgentApprovalException>(() => CreateService(db).RejectAsync(
            artifact.Run.Id, artifact.Approval.Id, artifact.Run.UserId, false, new(Guid.NewGuid())));

        Assert.Equal("approval_rejection_comment_required", exception.Code);
    }

    private static AgentApprovalService CreateService(EquityLensDbContext db) =>
        new(db, new AgentRunStateMachine(), new AgentNodeStateMachine());

    private static (AgentRun Run, AgentRunNode Node, AgentApprovalRequest Approval) SeedWaitingRun(EquityLensDbContext db)
    {
        var node = new AgentRunNode
        {
            Id = Guid.NewGuid(), NodeKey = "danger", TemplateNodeKey = "danger",
            NodeType = CriticReviewNodeTypes.LoadResearchRun, Status = AgentNodeStatuses.WaitingForApproval
        };
        var run = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), WorkflowType = AgentWorkflowTypes.CriticReview,
            AgentType = AgentTypes.Critic, Status = AgentRunStatuses.WaitingForApproval,
            WorkflowDefinitionJson = """{"version":1,"nodes":[],"edges":[]}""",
            Nodes = [node]
        };
        node.AgentRunId = run.Id;
        var approval = new AgentApprovalRequest
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = node.Id,
            NodeKey = node.NodeKey, NodeType = node.NodeType, Status = AgentApprovalStatuses.Pending,
            SideEffectLevel = "ExternalWrite", Reason = "需要批准", PolicySnapshotJson = "{}",
            Run = run, Node = node
        };
        run.Approvals.Add(approval);
        node.Approvals.Add(approval);
        db.AgentRuns.Add(run);
        return (run, node, approval);
    }

    private static EquityLensDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDb(options);
    }

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentApprovalRequest>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunWakeOutbox>().Property(x => x.Id).ValueGeneratedNever();
        }
    }

    private sealed class CountingNodeHandler : IAgentNodeHandler
    {
        public string NodeType => CriticReviewNodeTypes.LoadResearchRun;
        public int CallCount { get; private set; }
        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            context.Node.OutputJson = "{}";
            return Task.CompletedTask;
        }
    }
}
