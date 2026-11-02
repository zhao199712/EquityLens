using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ToolApprovalTests
{
    private const string Publish = """{"action":"call_tools","toolCalls":[{"callId":"publish","tool":"test_publish_note","arguments":{"text":"review me"}}]}""";

    [Fact]
    public async Task SideEffect_PausesWithoutWake_ApproveExecutesOnce_DuplicateResumeIsHarmless()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = await Seed(db); var tool = new PublishTool(); var chat = new Chat(Publish, "{\"action\":\"give_up\"}");
        var executor = Executor(db, chat, tool);
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal("WaitingForFeedback", run.Status);
        Assert.Null(run.LeaseOwner);
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
        Assert.Equal(0, tool.Effects);
        var approval = await db.AgentApprovalRequests.SingleAsync();
        Assert.Equal("ToolCall", approval.Scope);
        Assert.Equal(ResearchToolApproval.Hash("{\"text\":\"review me\"}"), approval.ArgumentsHash);
        var request = new DecideAgentApprovalRequest(Guid.NewGuid());
        var service = new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine());
        await service.ApproveAsync(run.Id, approval.Id, run.UserId, false, request);
        await service.ApproveAsync(run.Id, approval.Id, run.UserId, false, request);
        Assert.Single(await db.AgentRunWakeOutbox.ToListAsync());
        await executor.ExecuteAsync(run.Id, run.UserId);
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(1, tool.Effects);
        Assert.Equal("Succeeded", run.Status);
        Assert.NotNull(approval.ConsumedAtUtc);
        Assert.Contains(run.Events, x => x.EventType == "ToolApprovalRevalidated");
    }

    [Theory]
    [InlineData("arguments", "ApprovalArgumentsChanged")]
    [InlineData("expired", "ApprovalExpired")]
    [InlineData("visibility", "ToolNotVisible")]
    public async Task Approval_IsRevalidatedBeforeExecution(string change, string reason)
    {
        await using var db = RefactorTestDbContext.Create();
        var run = await Seed(db); var tool = new PublishTool(); var chat = new Chat(Publish, "{\"action\":\"give_up\"}");
        var executor = Executor(db, chat, tool);
        await executor.ExecuteAsync(run.Id, run.UserId);
        var approval = await db.AgentApprovalRequests.SingleAsync();
        await new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine())
            .ApproveAsync(run.Id, approval.Id, run.UserId, false, new(Guid.NewGuid()));
        if (change == "arguments") (await db.AgentToolCalls.SingleAsync()).ArgumentsJson = "{\"text\":\"changed\"}";
        if (change == "expired") approval.RequestedAtUtc = DateTime.UtcNow.AddDays(-2);
        if (change == "visibility") tool.VisibleNow = false;
        await db.SaveChangesAsync();
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(0, tool.Effects);
        Assert.Null(approval.ConsumedAtUtc);
        Assert.Equal(reason, (await db.AgentToolCalls.SingleAsync()).GuardReason);
        Assert.Contains(reason, chat.Prompts.Last());
    }

    [Fact]
    public async Task Reject_RequiresComment_ThenReturnsReasonToAgent()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = await Seed(db); var tool = new PublishTool(); var chat = new Chat(Publish, "{\"action\":\"give_up\"}");
        var executor = Executor(db, chat, tool);
        await executor.ExecuteAsync(run.Id, run.UserId);
        var approval = await db.AgentApprovalRequests.SingleAsync();
        var service = new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine());
        await Assert.ThrowsAsync<AgentApprovalException>(() => service.RejectAsync(run.Id, approval.Id, run.UserId, false, new(Guid.NewGuid())));
        Assert.Null(await service.ApproveAsync(run.Id, approval.Id, Guid.NewGuid(), false, new(Guid.NewGuid())));
        await service.RejectAsync(run.Id, approval.Id, run.UserId, false, new(Guid.NewGuid(), "內容未核實"));
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(0, tool.Effects);
        Assert.Contains(JsonNode.Parse(chat.Prompts.Last())!["history"]!.AsArray(), x => x!.GetValue<string>().Contains("內容未核實"));
    }

    [Fact]
    public async Task CrashAfterExternalEffect_ReplaysSameIdempotencyKey_NotTheEffect()
    {
        await using var db = RefactorTestDbContext.Create();
        using var crash = new CancellationTokenSource();
        var run = await Seed(db); var tool = new PublishTool { Crash = crash }; var chat = new Chat(Publish, "{\"action\":\"give_up\"}");
        var executor = Executor(db, chat, tool);
        await executor.ExecuteAsync(run.Id, run.UserId);
        var approval = await db.AgentApprovalRequests.SingleAsync();
        await new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine())
            .ApproveAsync(run.Id, approval.Id, run.UserId, false, new(Guid.NewGuid()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(run.Id, run.UserId, crash.Token));
        db.ChangeTracker.Clear();
        var stored = await db.AgentRuns.SingleAsync(x => x.Id == run.Id);
        stored.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        tool.Crash = null;
        await Executor(db, chat, tool).ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(2, tool.Calls);
        Assert.Equal(1, tool.Effects);
        Assert.Equal("Succeeded", stored.Status);
    }

    [Fact]
    public void ArgumentHash_IsIndependentOfObjectPropertyOrder()
    {
        Assert.Equal(ResearchToolApproval.Hash("{\"a\":1,\"b\":{\"x\":2,\"y\":3}}"),
            ResearchToolApproval.Hash("{\"b\":{\"y\":3,\"x\":2},\"a\":1}"));
    }

    private static async Task<AgentRun> Seed(EquityLensDbContext db)
    {
        var run = new AgentRun { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "Pending", WorkflowType = "ToolLoopFixture",
            BlackboardJson = AgentNodeJson.Serialize(new { researchRequest = new ResearchAskRequest("2330", "test") }),
            WorkflowDefinitionJson = """{"version":1,"nodes":[{"id":"loop","type":"ResearchAgentLoop","required":true,"executionPolicy":{"timeoutSeconds":30,"maxRetryCount":0}}],"edges":[]}""" };
        run.Nodes.Add(new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "loop", NodeType = ResearchAgentNodes.Loop, Status = "Pending" });
        db.Add(run); await db.SaveChangesAsync(); return run;
    }
    private static AgentRunExecutor Executor(EquityLensDbContext db, Chat chat, PublishTool tools) => AgentExecutorTestFactory.Create(db,
        new WorkflowGraphTopologyService(), new AgentRunGraphValidator(), new AgentRunStateMachine(), new AgentNodeStateMachine(),
        [new Handler(new ResearchAgentLoop(chat, tools, Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System))],
        NullLogger<AgentRunExecutor>.Instance, new AgentWorkflowCatalog());
    private sealed class Handler(ResearchAgentLoop loop) : IAgentNodeHandler
    {
        public string NodeType => ResearchAgentNodes.Loop;
        public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default) =>
            await loop.RunAsync(context, "Research", cancellationToken);
    }
    private sealed class Chat(params string[] actions) : IChatCompletionService
    {
        private readonly Queue<string> _actions = new(actions);
        public List<string> Prompts { get; } = [];
        public string Provider => "test"; public string Model => "test";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        { Prompts.Add(request.UserPrompt); return Task.FromResult(new ChatCompletionResult(_actions.Dequeue(), Model, 10, 10)); }
    }
    private sealed class PublishTool : IResearchLoopTools
    {
        public bool VisibleNow = true;
        public int Calls; public int Effects;
        public CancellationTokenSource? Crash;
        private readonly HashSet<string> _keys = [];
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => VisibleNow
            ? [new("test_publish_note", "test-only side effect", new(), RequiresApproval: true)] : [];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => VisibleNow ? null : "ToolNotVisible";
        public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
        {
            Calls++; Assert.NotNull(scope.IdempotencyKey);
            if (_keys.Add(scope.IdempotencyKey)) Effects++;
            if (Crash is not null) { Crash.Cancel(); ct.ThrowIfCancellationRequested(); }
            return Task.FromResult(new LoopToolResult([], "Published"));
        }
    }
}
