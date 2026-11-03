using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class WorkflowExecutionCompatibilityTests
{
    private static WorkflowExecutionCompatibility Retired() => new([], new AgentWorkflowCatalog());

    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("WaitingForFeedback")]
    [InlineData("WaitingForApproval")]
    public async Task RetiredQueueItem_FailsOnceWithoutExecutingOrRequeueing(string status)
    {
        await using var db = RefactorTestDbContext.Create();
        var run = new CriticReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        run.Status = status;
        db.Add(run); await db.SaveChangesAsync();
        var executor = AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(), [], NullLogger<AgentRunExecutor>.Instance, compatibility: Retired());
        Assert.Equal(AgentRunExecutionOutcome.Executed, await executor.ExecuteAsync(run.Id, run.UserId));
        Assert.Equal(AgentRunStatuses.Failed, run.Status);
        Assert.Equal(LegacyWorkflowRetiredException.Code, run.ErrorMessage);
        Assert.Null(run.LeaseOwner);
        Assert.All(run.Nodes, n => Assert.Equal(AgentNodeStatuses.Pending, n.Status));
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
        var count = run.Events.Count;
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(count, run.Events.Count);
    }

    [Fact]
    public async Task RetiredRun_WithAnotherWorkersLease_IsUntouched()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = new CriticReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        run.Status = AgentRunStatuses.Running; run.LeaseOwner = "other"; run.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        db.Add(run); await db.SaveChangesAsync();
        var executor = AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(), [], NullLogger<AgentRunExecutor>.Instance, compatibility: Retired());
        Assert.Equal(AgentRunExecutionOutcome.LeaseHeldByOther, await executor.ExecuteAsync(run.Id, run.UserId));
        Assert.Equal(AgentRunStatuses.Running, run.Status);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public void VersionAndRemovedNodeShape_AreBothChecked()
    {
        var provider = new CriticReviewWorkflowDefinitionProvider();
        var policy = new WorkflowExecutionCompatibility([provider], new AgentWorkflowCatalog());
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(policy.IsRetired(run));
        var original = run.WorkflowDefinitionJson;
        var json = JsonNode.Parse(original)!;
        json["version"] = -1;
        run.WorkflowDefinitionJson = json.ToJsonString();
        Assert.True(policy.IsRetired(run));
        json = JsonNode.Parse(original)!;
        json["nodes"]![0]!["type"] = "RetiredNode";
        run.WorkflowDefinitionJson = json.ToJsonString();
        Assert.True(policy.IsRetired(run));
    }

    [Fact]
    public async Task RetiredRun_RemainsReadableButCannotRetry_AndIsIsolated()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = new CriticReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        run.Status = AgentRunStatuses.Failed;
        db.Add(run); await db.SaveChangesAsync();
        var service = new AgentRunService(db, [], new AgentRunStateMachine(), new AgentNodeStateMachine(), new NoQueue(), compatibility: Retired());
        Assert.NotNull(await service.GetByIdAsync(run.Id, run.UserId));
        await Assert.ThrowsAsync<LegacyWorkflowRetiredException>(() => service.RetryAsync(run.Id, run.UserId));
        Assert.Null(await service.GetByIdAsync(run.Id, Guid.NewGuid()));
        Assert.Null(await service.RetryAsync(run.Id, Guid.NewGuid()));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("ResearchAgent")]
    [InlineData("PortfolioDiagnosis")]
    [InlineData("CriticReview")]
    public void CurrentProvidersAcceptOnlyCurrentVersionAndShape(string workflow)
    {
        IAgentWorkflowDefinitionProvider[] providers = [new EquityLens.Api.Services.Agents.Loop.ResearchAgentWorkflowDefinitionProvider(), new PortfolioDiagnosisWorkflowDefinitionProvider(), new CriticReviewWorkflowDefinitionProvider()];
        var policy = new WorkflowExecutionCompatibility(providers, new AgentWorkflowCatalog());
        var run = providers.Single(p => p.WorkflowType == workflow).CreateRun(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(policy.IsRetired(run));
        var original = run.WorkflowDefinitionJson; var definition = JsonNode.Parse(original)!;
        definition["version"] = definition["version"]!.GetValue<int>() - 1;
        run.WorkflowDefinitionJson = definition.ToJsonString(); Assert.True(policy.IsRetired(run));
        run.WorkflowDefinitionJson = original; run.Nodes.First().NodeType = "RemovedNode"; Assert.True(policy.IsRetired(run));
    }

    private sealed class NoQueue : IAgentRunQueue
    {
        public Task EnqueueAsync(AgentRunQueueMessage m, CancellationToken ct = default) => throw new InvalidOperationException("Must not enqueue");
        public Task<AgentRunQueueItem?> ReadNextAsync(string c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentRunQueueItem?> ReadStalePendingAsync(string c, TimeSpan t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AcknowledgeAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
