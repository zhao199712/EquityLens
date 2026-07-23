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

    [Fact]
    public async Task RunCard_PrefersResearchAnswerOverCriticSummary()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var researchId = Guid.NewGuid();
        db.ChatSessions.Add(new ChatSession { Id = sessionId, UserId = userId });
        db.ResearchRuns.Add(new ResearchRun
        {
            Id = researchId, UserId = userId, TraceId = "trace", Ticker = "2454",
            Question = "問題", Answer = "正式研究答案", RetrievalMode = "Auto", SourcePolicy = "Auto"
        });
        db.AgentRuns.Add(new AgentRun
        {
            Id = runId, UserId = userId, ResearchRunId = researchId,
            WorkflowType = AgentWorkflowTypes.ResearchInvestigation, AgentType = AgentTypes.Research,
            Status = AgentRunStatuses.Succeeded, OutputJson = """{"summary":"Critic 摘要"}"""
        });
        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(), ChatSessionId = sessionId, AgentRunId = runId,
            Role = "assistant", MessageType = "AgentRun", SequenceNumber = 0
        });
        await db.SaveChangesAsync();
        var service = new ConversationService(db, Agent(ConversationActions.DirectResponse, "x", null, new()),
            new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());

        var card = await service.GetRunCardAsync(userId, sessionId, runId);

        Assert.Equal("正式研究答案", card?.FinalAnswer);
    }

    [Fact]
    public async Task RouteWorkflow_PortfolioRequired_BecomesClarification()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var workflows = new FakeWorkflowQueries(
            failure: new AgentWorkflowQueryException("portfolio_required", "目前沒有可供診斷的投資組合。"));
        var service = new ConversationService(db,
            Agent(ConversationActions.RouteWorkflow, "幫你診斷投組。", "我的投資組合最近一年的風險如何？", new JsonObject()),
            workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());

        var result = await service.ProcessAsync(userId, session.Id, new("我的投資組合最近一年的風險如何？", Guid.NewGuid()));

        Assert.Equal(ConversationActions.AskClarification, result.Action);
        Assert.Contains("目前沒有可供診斷的投資組合", result.Content);
        Assert.Contains("投資組合」頁面", result.Content);
        Assert.Null(result.RunCard);
        var turn = await db.ConversationTurns.SingleAsync();
        Assert.Equal("Succeeded", turn.Status);
        Assert.Null(turn.AgentRunId);
        var message = await db.ChatMessages.SingleAsync(x => x.Role == "assistant");
        Assert.Equal("Text", message.MessageType);
    }

    [Fact]
    public async Task RouteWorkflow_SecurityNotFound_BecomesClarificationWithoutHint()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        var workflows = new FakeWorkflowQueries(
            failure: new AgentWorkflowQueryException("security_not_found", "找不到「某某公司」對應的證券，請改用股票代號或完整公司名稱。"));
        var service = new ConversationService(db,
            Agent(ConversationActions.RouteWorkflow, "幫你研究。", "分析某某公司", new JsonObject()),
            workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());

        var result = await service.ProcessAsync(userId, session.Id, new("分析某某公司", Guid.NewGuid()));

        Assert.Equal(ConversationActions.AskClarification, result.Action);
        Assert.Contains("找不到「某某公司」", result.Content);
        Assert.DoesNotContain("投資組合」頁面", result.Content);
        Assert.Equal("Succeeded", (await db.ConversationTurns.SingleAsync()).Status);
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

    private sealed class FakeWorkflowQueries(Guid? runId = null, AgentWorkflowQueryException? failure = null) : IAgentWorkflowQueryService
    {
        public int Calls { get; private set; }
        public string? Question { get; private set; }
        public Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Question = request.Question;
            if (failure is not null) throw failure;
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
