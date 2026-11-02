using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Integration;

[Collection(PortfolioTransactionPostgresCollection.Name)]
public sealed class ToolApprovalPostgresTests(PostgresTransactionDatabaseFixture fixture)
{
    private EquityLensDbContext Db() => new(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseNpgsql(fixture.ConnectionString!, x => x.UseVector()).Options);

    [PostgresFact]
    public async Task ConcurrentToolDecisions_OnlyOneWins_AndPublishesOneWake()
    {
        await using var seed = Db();
        var user = new AppUser { Id = Guid.NewGuid(), Email = $"approval-{Guid.NewGuid():N}@test.invalid", DisplayName = "Approval test" };
        var run = new AgentRun { Id = Guid.NewGuid(), UserId = user.Id, WorkflowType = "ToolLoopFixture", Status = "WaitingForFeedback" };
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "loop", NodeType = "ResearchAgentLoop", Status = "WaitingForFeedback" };
        run.Nodes.Add(node);
        var tool = new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = node.Id, ToolName = "test_publish_note", Status = "AwaitingApproval" };
        var approval = new AgentApprovalRequest { Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = node.Id,
            AgentToolCallId = tool.Id, Scope = "ToolCall", NodeKey = "loop", NodeType = node.NodeType, Status = "Pending" };
        seed.AddRange(user, run, tool, approval); await seed.SaveChangesAsync();
        await using var first = Db(); await using var second = Db();
        // Both reviewers have read the same pending version before either decision is written.
        await first.AgentApprovalRequests.Include(x => x.Run).Include(x => x.Node).SingleAsync(x => x.Id == approval.Id);
        await second.AgentApprovalRequests.Include(x => x.Run).Include(x => x.Node).SingleAsync(x => x.Id == approval.Id);
        async Task<bool> Decide(EquityLensDbContext db, bool approve)
        {
            var service = new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine());
            try
            {
                if (approve) await service.ApproveAsync(run.Id, approval.Id, user.Id, false, new(Guid.NewGuid()));
                else await service.RejectAsync(run.Id, approval.Id, user.Id, false, new(Guid.NewGuid(), "not verified"));
                return true;
            }
            catch (AgentApprovalException) { return false; }
        }
        var outcomes = await Task.WhenAll(Decide(first, true), Decide(second, false));
        Assert.Single(outcomes, x => x);
        await using var verify = Db();
        Assert.Equal(1, await verify.AgentRunWakeOutbox.CountAsync(x => x.AgentRunId == run.Id));
        Assert.Equal("Running", (await verify.AgentRuns.SingleAsync(x => x.Id == run.Id)).Status);
        Assert.Equal("Pending", (await verify.AgentRunNodes.SingleAsync(x => x.Id == node.Id)).Status);
        Assert.NotEqual("Pending", (await verify.AgentApprovalRequests.SingleAsync(x => x.Id == approval.Id)).Status);
    }
}
