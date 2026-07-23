using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task DirectResponse_PersistsTurnAndDoesNotCreateWorkflow()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var workflows = new FakeWorkflowQueries();
        var service = new ConversationService(db,
            Agent(ConversationActions.DirectResponse, "你好！", null, new JsonObject { ["topic"] = "greeting" }),
            workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());

        var result = await service.ProcessAsync(userId, session.Id, new("你好", Guid.NewGuid()));

        Assert.Equal(ConversationActions.DirectResponse, result.Action);
        Assert.Equal(0, workflows.Calls);
        Assert.Equal(2, await db.ChatMessages.CountAsync());
        var turn = await db.ConversationTurns.SingleAsync();
        Assert.Equal("Succeeded", turn.Status);
        Assert.Equal(1, (await db.ChatSessions.SingleAsync()).ContextVersion);
    }

    [Fact]
    public async Task RouteWorkflow_UsesStandaloneQueryAndPersistsRunCardMessage()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var runId = Guid.NewGuid();
        var workflows = new FakeWorkflowQueries(runId);
        var service = new ConversationService(db,
            Agent(ConversationActions.RouteWorkflow, "已建立研究。", "分析聯發科2026年第二季指引", new JsonObject { ["security"] = "聯發科" }),
            workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());

        await service.ProcessAsync(userId, session.Id, new("那第二季呢？", Guid.NewGuid()));

        Assert.Equal("分析聯發科2026年第二季指引", workflows.Question);
        var message = await db.ChatMessages.SingleAsync(x => x.Role == "assistant");
        Assert.Equal("AgentRun", message.MessageType);
        Assert.Equal(runId, message.AgentRunId);
    }

    [Fact]
    public async Task ResetContext_ClearsPreviousState()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId, ContextJson = """{"security":"聯發科"}""" };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new ConversationService(db,
            Agent(ConversationActions.ResetContext, "已清除本次對話脈絡。", null, new JsonObject()),
            new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());

        await service.ProcessAsync(userId, session.Id, new("忘記前面", Guid.NewGuid()));

        var context = JsonNode.Parse((await db.ChatSessions.SingleAsync()).ContextJson)!.AsObject();
        Assert.False(context.ContainsKey("security"));
    }

    private static IConversationAgent Agent(string action, string response, string? query, JsonObject patch) =>
        new FakeConversationAgent(new(action, response, query, action, "high", patch, string.Empty,
            "test", "test", 1, 1, 1));

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeConversationAgent(ConversationAgentDecision decision) : IConversationAgent
    {
        public Task<ConversationAgentDecision> DecideAsync(ConversationAgentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(decision);
    }

    private sealed class FakeWorkflowQueries(Guid? runId = null) : IAgentWorkflowQueryService
    {
        public int Calls { get; private set; }
        public string? Question { get; private set; }
        public Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Question = request.Question;
            return Task.FromResult(new AgentWorkflowQueryCreatedResponse(runId ?? Guid.NewGuid(), Guid.NewGuid(),
                AgentWorkflowTypes.ResearchInvestigation, AgentRunStatuses.Pending, "test", "test",
                "research-investigation", "一般個股研究", "high", request.Question,
                new("TW", "equity", "standard", null, null, "zh-TW"), [], []));
        }
    }

    private sealed class FakeAgentRuns : IAgentRunService
    {
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(Guid userId, Guid evidenceRemediationRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubmitAgentFeedbackResponse> SubmitFeedbackAsync(Guid runId, Guid userId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListChildrenAsync(Guid parentRunId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
