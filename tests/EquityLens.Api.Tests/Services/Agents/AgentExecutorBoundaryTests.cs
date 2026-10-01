using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentExecutorBoundaryTests
{
    [Fact]
    public async Task BatchYield_PersistsOneWakeAndResumesWithoutReexecutingCompletedNodes()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = CreateRun(8);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new StampHandler();
        var executor = CreateExecutor(db, handler);

        await executor.ExecuteAsync(run.Id, run.UserId);

        Assert.Equal(AgentRunStatuses.Running, run.Status);
        Assert.Equal(6, handler.Calls);
        Assert.Equal(6, run.Nodes.Count(x => x.Status == AgentNodeStatuses.Succeeded));
        Assert.Null(run.LeaseOwner);
        Assert.Null(run.LeaseExpiresAtUtc);
        var wake = Assert.Single(await db.AgentRunWakeOutbox.ToListAsync());
        Assert.Equal(run.OrchestrationVersion, wake.OrchestrationVersion);
        var completedAt = run.Nodes.Take(6).Select(x => x.CompletedAtUtc).ToArray();

        await executor.ExecuteAsync(run.Id, run.UserId);

        Assert.Equal(AgentRunStatuses.Succeeded, run.Status);
        Assert.Equal(8, handler.Calls);
        Assert.All(run.Nodes, node => Assert.Equal(AgentNodeStatuses.Succeeded, node.Status));
        Assert.Equal(completedAt, run.Nodes.Take(6).Select(x => x.CompletedAtUtc));
        Assert.Single(await db.AgentRunWakeOutbox.ToListAsync());
    }

    [Fact]
    public async Task ActiveLease_DoesNotExecuteOrMutateTheRun()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = CreateRun(1);
        run.Status = AgentRunStatuses.Running;
        run.LeaseOwner = "another-worker";
        run.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new StampHandler();

        await CreateExecutor(db, handler).ExecuteAsync(run.Id, run.UserId);

        Assert.Equal(0, handler.Calls);
        Assert.Equal("another-worker", run.LeaseOwner);
        Assert.Equal(0, run.OrchestrationVersion);
        Assert.Equal(AgentNodeStatuses.Pending, run.Nodes.Single().Status);
        Assert.Empty(await db.AgentRunEvents.ToListAsync());
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task RetryableNodeFailure_PreservesBlackboardAndTokenAccounting()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = CreateRun(1, retries: 1);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new StampHandler(failFirstAttempt: true);

        await CreateExecutor(db, handler).ExecuteAsync(run.Id, run.UserId);

        var node = run.Nodes.Single();
        Assert.Equal(AgentRunStatuses.Succeeded, run.Status);
        Assert.Equal(2, handler.Calls);
        Assert.Null(node.ErrorCode);
        Assert.Equal(0, node.InputBlackboardVersion);
        Assert.Equal(0, node.OutputBlackboardVersion);
        Assert.Contains("stamp", node.ProducedBlackboardKeys!);
        Assert.Equal(1, AgentNodeJson.ParseBlackboard(run.BlackboardJson)["blackboardVersion"]!.GetValue<int>());
        Assert.Equal(10, run.TotalInputTokens);
        Assert.Equal(20, run.TotalOutputTokens);
        Assert.Equal(.01m, run.TotalEstimatedCostUsd);
        Assert.Single(await db.AgentRunEvents.Where(x => x.EventType == AgentEventTypes.NodeFailed).ToListAsync());
        Assert.Single(await db.AgentRunEvents.Where(x => x.EventType == AgentEventTypes.NodeCompleted).ToListAsync());
    }

    [Fact]
    public async Task NodeTimeout_FailsWithoutRetryOrWake()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = CreateRun(1, timeoutSeconds: 1, retries: 2);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var handler = new StampHandler(waitForTimeout: true);

        await CreateExecutor(db, handler).ExecuteAsync(run.Id, run.UserId);

        var node = run.Nodes.Single();
        Assert.Equal(AgentRunStatuses.Failed, run.Status);
        Assert.Equal(AgentNodeStatuses.Failed, node.Status);
        Assert.Equal("timeout", node.ErrorCode);
        Assert.False(node.ErrorRetryable);
        Assert.Equal(1, handler.Calls);
        Assert.Null(run.LeaseOwner);
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
    }

    private static AgentRunExecutor CreateExecutor(RefactorTestDbContext db, IAgentNodeHandler handler) =>
        AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(), [handler], NullLogger<AgentRunExecutor>.Instance);

    private static AgentRun CreateRun(int nodeCount, int timeoutSeconds = 120, int retries = 0)
    {
        var run = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), WorkflowType = "ExecutorBoundaryTest",
            AgentType = AgentTypes.Critic, Status = AgentRunStatuses.Pending, BlackboardJson = "{}"
        };
        run.Nodes = Enumerable.Range(0, nodeCount).Select(i => new AgentRunNode
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = $"node-{i}",
            NodeType = StampHandler.Type, Status = AgentNodeStatuses.Pending
        }).ToList();
        run.WorkflowDefinitionJson = JsonSerializer.Serialize(new
        {
            version = 1,
            nodes = run.Nodes.Select(node => new
            {
                id = node.NodeKey, type = node.NodeType, required = true,
                executionPolicy = new { timeoutSeconds, maxRetryCount = retries },
                approvalPolicy = new { requiresHumanApproval = false, sideEffectLevel = "ReadOnly", reason = "fixture" }
            }),
            edges = Enumerable.Range(1, nodeCount - 1).Select(i => new { from = $"node-{i - 1}", to = $"node-{i}" })
        });
        return run;
    }

    private sealed class StampHandler(bool failFirstAttempt = false, bool waitForTimeout = false) : IAgentNodeHandler
    {
        public const string Type = "BoundaryStamp";
        public string NodeType => Type;
        public int Calls { get; private set; }

        public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            if (waitForTimeout) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (failFirstAttempt && Calls == 1)
                throw new AgentNodeException("temporary", AgentNodeErrorCategories.TransientFailure, "Retry fixture", retryable: true);
            var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
            board["stamp"] = context.Node.NodeKey;
            context.Run.BlackboardJson = board.ToJsonString();
            context.Node.InputTokens = 10;
            context.Node.OutputTokens = 20;
            context.Node.EstimatedCostUsd = .01m;
        }
    }
}
