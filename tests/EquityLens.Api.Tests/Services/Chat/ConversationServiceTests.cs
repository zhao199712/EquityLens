using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Chat;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed partial class ConversationServiceTests
{
    [Fact]
    public async Task RevisePortfolio_UsesLinkedWorkflowInsteadOfResearchFeedback()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid(); var parentId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user,
            ContextJson = new JsonObject { ["lastAgentRunId"] = parentId, ["lastResearchRunId"] = Guid.NewGuid() }.ToJsonString() };
        db.ChatSessions.Add(session);
        db.AgentRuns.Add(new AgentRun { Id = parentId, UserId = user, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis, Status = AgentRunStatuses.Succeeded });
        await db.SaveChangesAsync();
        var workflows = new FakeWorkflowQueries();
        var service = new ConversationService(db, Agent(ConversationActions.RevisePreviousRun, "重新診斷", "國巨降至30%", new JsonObject()), workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());
        await service.ProcessAsync(user, session.Id, new("請修正投組分析", Guid.NewGuid()));
        Assert.Equal(parentId, workflows.ParentId);
        Assert.Equal(1, workflows.Calls);
        Assert.True(JsonNode.Parse(session.ContextJson)!.AsObject().ContainsKey("lastResearchRunId"));
    }

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
            WorkflowType = LegacyWorkflowReadModel.Research, AgentType = AgentTypes.Research,
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

    [Fact]
    public async Task UnifiedRoute_CreatesResearchWithOneModelCall()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        db.ChatSessions.Add(session); db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2330", Name = "台積電", IsActive = true }); await db.SaveChangesAsync();
        var chat = new CountingChat(); var router = new InvestmentResearchRouter(chat); var runs = new FakeAgentRuns();
        var service = new ConversationService(db, router, new AgentWorkflowQueryService(db, router, runs), runs, new AgentWorkflowCatalog());
        var result = await service.ProcessAsync(user, session.Id, new("台積電營收如何", Guid.NewGuid()));
        Assert.Equal(ConversationActions.RouteWorkflow, result.Action); Assert.Equal(1, chat.Calls);
        Assert.Equal(AgentWorkflowTypes.ResearchAgent, runs.CreatedWorkflow);
        Assert.Contains("2330", chat.Last!.UserPrompt); Assert.Contains("tickerCandidates", chat.Last.UserPrompt);
    }

    [Theory]
    [InlineData("那毛利率呢", "RouteWorkflow")]
    [InlineData("重置對話", "ResetContext")]
    public async Task TypedFollowupIsClassified_OnlyResetBypassesModel(string message, string expected)
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var parent = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = new JsonObject { ["lastAgentRunId"] = parent }.ToJsonString() };
        db.ChatSessions.Add(session); db.AgentRuns.Add(new AgentRun { Id = parent, UserId = user, WorkflowType = AgentWorkflowTypes.ResearchAgent, Status = AgentRunStatuses.Succeeded }); await db.SaveChangesAsync();
        var chat = new CountingChat(); var workflows = new FakeWorkflowQueries();
        var service = new ConversationService(db, new InvestmentResearchRouter(chat), workflows, new FakeAgentRuns(), new AgentWorkflowCatalog());
        var result = await service.ProcessAsync(user, session.Id, new(message, Guid.NewGuid()));
        Assert.Equal(expected, result.Action); Assert.Equal(expected == "RouteWorkflow" ? 1 : 0, chat.Calls);
        if (expected == "RouteWorkflow") Assert.Equal(parent, workflows.ParentId);
    }

    [Fact]
    public async Task IdempotentRequest_DoesNotLeakOtherUsersTurn()
    {
        await using var db = CreateDb(); var owner = Guid.NewGuid(); var session = new ChatSession { Id = Guid.NewGuid(), UserId = owner };
        db.ChatSessions.Add(session); await db.SaveChangesAsync(); var request = new SendMessageRequest("你好", Guid.NewGuid());
        var service = new ConversationService(db, Agent(ConversationActions.DirectResponse, "private", null, new()), new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());
        await service.ProcessAsync(owner, session.Id, request);
        await Assert.ThrowsAsync<ConversationException>(() => service.ProcessAsync(Guid.NewGuid(), session.Id, request));
    }

    [Theory]
    [InlineData("高beta")]
    [InlineData("2")]
    public async Task UserSelection_PersistsSwitchesAndResetsWithoutTrustingModel(string choice)
    {
        await using var db = CreateDb(); var user = Guid.NewGuid();
        var a = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "低波動" };
        var b = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "高beta" };
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        db.Portfolios.AddRange(a, b); db.ChatSessions.Add(session); await db.SaveChangesAsync();
        var agent = new PortfolioAgent(a.Id); var runs = new FakeAgentRuns();
        var service = new ConversationService(db, agent, new AgentWorkflowQueryService(db, new InvestmentResearchRouter(new CountingChat()), runs), runs, new AgentWorkflowCatalog());
        var inventory = await service.ProcessAsync(user, session.Id, new("我有幾個投資組合", Guid.NewGuid()));
        Assert.Contains("高beta", inventory.Content);
        await service.ProcessAsync(user, session.Id, new(choice, Guid.NewGuid()));
        Assert.Equal(b.Id.ToString(), JsonNode.Parse(session.ContextJson)!["activePortfolioId"]!.ToString());
        await service.ProcessAsync(user, session.Id, new("這個波動率如何", Guid.NewGuid()));
        Assert.Equal(b.Id, runs.PortfolioId); // model tried selecting A in both ID and rewritten question
        Assert.Equal(AgentWorkflowTypes.PortfolioDiagnosis, runs.CreatedWorkflow);
        await service.ProcessAsync(user, session.Id, new("低波動的波動率如何", Guid.NewGuid()));
        Assert.Equal(a.Id, runs.PortfolioId);
        await service.ProcessAsync(user, session.Id, new("重置對話", Guid.NewGuid()));
        Assert.False(JsonNode.Parse(session.ContextJson)!.AsObject().ContainsKey("activePortfolioId"));
        var ambiguous = await service.ProcessAsync(user, session.Id, new("這個波動率如何", Guid.NewGuid()));
        Assert.Equal(ConversationActions.AskClarification, ambiguous.Action);
        Assert.Contains("高beta", ambiguous.Content);
        Assert.DoesNotContain("建立投資組合", ambiguous.Content);
        Assert.False(JsonNode.Parse(session.ContextJson)!.AsObject().ContainsKey("activePortfolioId"));
    }

    [Theory]
    [InlineData("第一個")]
    [InlineData("A")]
    [InlineData("前者")]
    [InlineData("1")]
    public async Task PortfolioChoices_AreOfferedAndNaturalOrdinalsSelectTheListedOption(string choice)
    {
        await using var db = CreateDb(); var user = Guid.NewGuid();
        var a = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "低波動" };
        var b = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "高beta" };
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        db.Portfolios.AddRange(a, b); db.ChatSessions.Add(session); await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new ConversationService(db, new PortfolioAgent(a.Id), new AgentWorkflowQueryService(db, new InvestmentResearchRouter(new CountingChat()), runs), runs, new AgentWorkflowCatalog());
        var inventory = await service.ProcessAsync(user, session.Id, new("我有幾個投資組合", Guid.NewGuid()));
        Assert.NotNull(inventory.Choices);
        var first = inventory.Choices![0] == a.Name ? a : b;
        await service.ProcessAsync(user, session.Id, new(choice, Guid.NewGuid()));
        Assert.Equal(first.Id.ToString(), JsonNode.Parse(session.ContextJson)!["activePortfolioId"]!.ToString());
        Assert.False(JsonNode.Parse(session.ContextJson)!.AsObject().ContainsKey("pendingChoices"));
    }

    [Theory]
    [InlineData("回到剛剛那個高波動的，如果遇到 2008 會跌多少？", "高波動科技")]
    [InlineData("那個金融科技的呢", "金融科技投資組合")]
    [InlineData("那穩健的呢", null)]
    [InlineData("優化投資組合", null)]
    [InlineData("科技股會怎樣", null)]
    public void PartialPortfolioName_SelectsOnlyAUniqueFragment(string message, string? expected)
    {
        var options = new[] { (Guid.NewGuid(), "高波動科技"), (Guid.NewGuid(), "穩健金融"), (Guid.NewGuid(), "金融科技投資組合") };
        var named = PortfolioSelection.NamedOrPartial(message, options).ToArray();
        if (expected is null) Assert.Empty(named);
        else Assert.Equal(options.Single(o => o.Item2 == expected).Item1, Assert.Single(named));
    }

    [Theory]
    [InlineData("第二個", 2, 2)]
    [InlineData("B", 2, 2)]
    [InlineData("後者", 3, 3)]
    [InlineData("第三個", 2, null)]
    [InlineData("我想看第一個方案的風險", 2, null)]
    public void ChoiceIndex_ParsesOnlyStandaloneOrdinals(string content, int count, int? expected) =>
        Assert.Equal(expected, ConversationService.ChoiceIndex(content, count));

    [Fact]
    public async Task StaleProcessingTurn_DoesNotLockTheSessionForever()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        db.ChatSessions.Add(session);
        db.ConversationTurns.Add(new ConversationTurn { Id = Guid.NewGuid(), ChatSessionId = session.Id, RequestId = Guid.NewGuid(),
            Status = "Processing", CreatedAtUtc = DateTime.UtcNow - ConversationService.StaleTurnTimeout - TimeSpan.FromMinutes(1) });
        await db.SaveChangesAsync();
        var service = new ConversationService(db, Agent(ConversationActions.DirectResponse, "你好", null, new()), new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());
        var result = await service.ProcessAsync(user, session.Id, new("你好", Guid.NewGuid()));
        Assert.Equal(ConversationActions.DirectResponse, result.Action);
        db.ConversationTurns.Add(new ConversationTurn { Id = Guid.NewGuid(), ChatSessionId = session.Id, RequestId = Guid.NewGuid(), Status = "Processing", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConversationException>(() => service.ProcessAsync(user, session.Id, new("再一次", Guid.NewGuid())));
    }

    [Fact]
    public async Task PreviouslySelectedPortfolio_IsRevalidatedAfterOwnershipChanges()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var a = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = new JsonObject { ["activePortfolioId"] = a }.ToJsonString() };
        db.ChatSessions.Add(session);
        db.Portfolios.AddRange(new Portfolio { Id = a, OwnerUserId = Guid.NewGuid(), Name = "別人" },
            new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "甲" }, new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "乙" });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var agent = new PortfolioAgent(null);
        var service = new ConversationService(db, agent, new AgentWorkflowQueryService(db, new InvestmentResearchRouter(new CountingChat()), runs), runs, new AgentWorkflowCatalog());
        var result = await service.ProcessAsync(user, session.Id, new("波動率如何", Guid.NewGuid()));
        Assert.Equal(ConversationActions.AskClarification, result.Action);
        Assert.Null(runs.PortfolioId);
        Assert.DoesNotContain("別人", result.Content);
        Assert.False(JsonNode.Parse(session.ContextJson)!.AsObject().ContainsKey("activePortfolioId"));
    }

    private sealed class PortfolioAgent(Guid? inventedId) : IConversationAgent
    {
        public Task<ConversationAgentDecision> DecideAsync(ConversationAgentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversationAgentDecision(ConversationActions.RouteWorkflow, "分析", "分析低波動的波動率", "test", "high",
                new JsonObject { ["activePortfolioId"] = inventedId?.ToString(), ["portfolioOptions"] = new JsonArray(inventedId?.ToString()) }, "", "test", "test", 0, 0, 0,
                InvestmentResearchRouter.Fixed(AgentWorkflowTypes.PortfolioDiagnosis, input.Message, inventedId)));
    }

    private sealed class CountingChat : IChatCompletionService
    {
        public int Calls; public ChatCompletionRequest? Last;
        public string Provider => "test"; public string Model => "configured";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        { Calls++; Last = request; return Task.FromResult(new ChatCompletionResult("""{"action":"route","workflow":"Research","params":{"ticker":"2330"},"confidence":"high","missing":[]}""", "test", 1, 1)); }
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
        public Guid? ParentId { get; private set; }
        public InvestmentResearchRouteDecision? Decision { get; private set; }
        public Task<AgentWorkflowQueryCreatedResponse> CreateFromDecisionAsync(Guid userId, CreateAgentWorkflowQueryRequest request, InvestmentResearchRouteDecision decision, CancellationToken cancellationToken = default)
        { Decision = decision; return CreateAsync(userId, request, cancellationToken); }
        public Task<AgentWorkflowQueryCreatedResponse> CreateAsync(Guid userId, CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Question = request.Question; ParentId = request.ParentAgentRunId;
            if (failure is not null) throw failure;
            return Task.FromResult(new AgentWorkflowQueryCreatedResponse(runId ?? Guid.NewGuid(), Guid.NewGuid(),
                LegacyWorkflowReadModel.Research, AgentRunStatuses.Pending, "test", "test",
                "research-investigation", "一般個股研究", "high", request.Question,
                new("TW", "equity", "standard", null, null, "zh-TW"), [], []));
        }
    }

    private sealed class FakeAgentRuns : IAgentRunService
    {
        public string? CreatedWorkflow;
        public Guid? PortfolioId;
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { CreatedWorkflow = AgentWorkflowTypes.ResearchAgent; return Task.FromResult((new AgentRunSummaryResponse(Guid.NewGuid(), CreatedWorkflow, "Research", AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0), Guid.NewGuid())); }
        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { PortfolioId = portfolioId; CreatedWorkflow = AgentWorkflowTypes.PortfolioDiagnosis; return Task.FromResult(new AgentRunSummaryResponse(Guid.NewGuid(), CreatedWorkflow, "Portfolio", AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0)); }
        public Task<SubmitAgentFeedbackResponse> SubmitFeedbackAsync(Guid runId, Guid userId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListChildrenAsync(Guid parentRunId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> DecideApprovalAsync(Guid runId, Guid userId, string decision, string? comment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
