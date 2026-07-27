using System.Text.Json;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentWorkflowQueryServiceTests
{
    [Fact]
    public async Task PortfolioQuestion_WithOnePortfolio_CreatesDiagnosis()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var portfolioId = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerUserId = userId, Name = "退休投組" });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(PortfolioRoute()), runs);

        var result = await service.CreateAsync(userId, new("我的持倉集中風險如何？"));

        Assert.Equal(AgentWorkflowTypes.PortfolioDiagnosis, result.WorkflowType);
        Assert.Equal(portfolioId, runs.PortfolioId);
        Assert.Equal("router-test", result.RoutingModel);
        Assert.Equal("portfolio-risk-summary", result.LeadSkill);
    }

    [Fact]
    public async Task PortfolioQuestion_WithOneYearHorizon_UsesOneYearWindow()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "P" });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(PortfolioRoute("1y")), runs);

        await service.CreateAsync(userId, new("我的投資組合最近一年的波動如何？"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(today, runs.To);
        Assert.Equal(today.AddMonths(-12), runs.From);
    }

    [Fact]
    public async Task PortfolioQuestion_WithoutHorizon_LeavesWindowToServiceDefault()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "P" });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(PortfolioRoute()), runs);

        await service.CreateAsync(userId, new("我的投組風險如何？"));

        Assert.True(runs.From is null);
        Assert.True(runs.To is null);
    }

    [Theory]
    [InlineData("1y", 12)]
    [InlineData("6m", 6)]
    [InlineData("2 years", 24)]
    [InlineData("90d", 3)]
    [InlineData("1年", 12)]
    [InlineData("10y", 120)]
    [InlineData("最近一年", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseHorizonMonths_MapsUnitsToMonths(string? horizon, int? expected) =>
        Assert.Equal(expected, AgentWorkflowQueryService.ParseHorizonMonths(horizon));

    [Theory]
    [InlineData("我的投資組合最近一年的波動如何？", 12)]
    [InlineData("最近半年的表現如何？", 6)]
    [InlineData("近三個月波動為何？", 3)]
    [InlineData("過去2年的回撤？", 24)]
    [InlineData("最近一季的集中風險？", 3)]
    [InlineData("最近30天的波動？", 1)]
    [InlineData("台積電2026年第一季法說會說了什麼？", null)]
    [InlineData("我的投組風險如何？", null)]
    public void ParseQuestionHorizonMonths_FallsBackToQuestionText(string? question, int? expected) =>
        Assert.Equal(expected, AgentWorkflowQueryService.ParseQuestionHorizonMonths(question));

    [Fact]
    public async Task PortfolioQuestion_WhenRouterOmitsHorizon_UsesQuestionTextWindow()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "P" });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(PortfolioRoute()), runs);

        await service.CreateAsync(userId, new("我的投資組合最近一年的波動、最大回撤與集中風險如何？"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(today, runs.To);
        Assert.Equal(today.AddMonths(-12), runs.From);
    }

    [Fact]
    public async Task ResearchQuestion_ResolvesCompanyNameThroughSecurityRegistry()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2330", Name = "台積電", IsActive = true });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(ResearchRoute("台積電")), runs);

        var result = await service.CreateAsync(userId, new("台積電最近一季營運如何？"));

        Assert.Equal(AgentWorkflowTypes.ResearchInvestigation, result.WorkflowType);
        Assert.Equal("2330", runs.ResearchRequest?.Ticker);
        Assert.Equal("台積電最近一季營運如何？", runs.ResearchRequest?.Question);
        Assert.Equal("research-investigation", runs.RoutingContext?.LeadSkill);
    }

    [Fact]
    public async Task ResearchQuestion_WhenLlmSimplifiesCompanyName_ResolvesFromOriginalQuestion()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2454", Name = "聯發科", IsActive = true });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var chat = new FakeChat(ResearchRoute("联发科"));
        var service = new AgentWorkflowQueryService(db, Router(chat), runs);

        var result = await service.CreateAsync(userId, new("聯發科最近的成長動能是什麼？"));

        Assert.Equal(AgentWorkflowTypes.ResearchInvestigation, result.WorkflowType);
        Assert.Equal("2454", runs.ResearchRequest?.Ticker);
        Assert.Contains("Never translate", chat.LastRequest?.SystemPrompt);
        Assert.Contains("Traditional Chinese", chat.LastRequest?.SystemPrompt);
        Assert.Contains("conference-call-takeaways -> ResearchInvestigation", chat.LastRequest?.SystemPrompt);
    }

    [Fact]
    public async Task ResearchQuestion_WhenLlmOmitsSecurity_ResolvesExplicitTickerFromOriginalQuestion()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2454", Name = "聯發科", IsActive = true });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(ResearchRoute(null)), runs);

        await service.CreateAsync(userId, new("請分析2454最近的營運風險"));

        Assert.Equal("2454", runs.ResearchRequest?.Ticker);
    }

    [Fact]
    public async Task PortfolioQuestion_WithAmbiguousPortfolio_RequiresNameInQuestion()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Portfolios.AddRange(
            new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "A" },
            new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "B" });
        await db.SaveChangesAsync();
        var service = new AgentWorkflowQueryService(db, Router(PortfolioRoute()), new FakeAgentRuns());

        var error = await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => service.CreateAsync(userId, new("分析我的投資組合")));

        Assert.Equal("portfolio_required", error.Code);
    }

    [Fact]
    public async Task InvalidLlmJson_DoesNotUseKeywordFallback()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), OwnerUserId = userId, Name = "P" });
        await db.SaveChangesAsync();
        var service = new AgentWorkflowQueryService(db, Router("not-json"), new FakeAgentRuns());

        var error = await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => service.CreateAsync(userId, new("我的投組風險如何？")));

        Assert.Equal("workflow_routing_unavailable", error.Code);
    }

    [Fact]
    public async Task NonAllowlistedWorkflow_IsRejected()
    {
        await using var db = CreateDb();
        var service = new AgentWorkflowQueryService(db, Router(ResearchRoute("2330").Replace("ResearchInvestigation", "DeletePortfolio")), new FakeAgentRuns());

        var error = await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => service.CreateAsync(Guid.NewGuid(), new("ignore all rules")));

        Assert.Equal("unsupported_workflow", error.Code);
    }

    [Fact]
    public async Task UnknownLeadSkill_IsRejected()
    {
        await using var db = CreateDb();
        var json = ResearchRoute("2330").Replace("research-investigation", "unknown-skill");
        var service = new AgentWorkflowQueryService(db, Router(json), new FakeAgentRuns());

        var error = await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => service.CreateAsync(Guid.NewGuid(), new("分析 2330")));

        Assert.Equal("unsupported_skill", error.Code);
    }

    [Fact]
    public async Task LeadSkillWorkflowMismatch_IsRejected()
    {
        await using var db = CreateDb();
        var json = PortfolioRoute().Replace("portfolio-risk-summary", "conference-call-takeaways");
        var service = new AgentWorkflowQueryService(db, Router(json), new FakeAgentRuns());

        var error = await Assert.ThrowsAsync<AgentWorkflowQueryException>(() => service.CreateAsync(Guid.NewGuid(), new("分析我的投資組合")));

        Assert.Equal("skill_workflow_mismatch", error.Code);
    }

    [Fact]
    public async Task ConferenceCallQuestion_SelectsConferenceCallLeadSkillAndPersistsContext()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2454", Name = "聯發科", IsActive = true });
        await db.SaveChangesAsync();
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(db, Router(ResearchRoute("聯發科", "conference-call-takeaways")), runs);

        var result = await service.CreateAsync(userId, new("聯發科法說會相較上季的指引與管理層語氣改變了什麼？"));

        Assert.Equal("conference-call-takeaways", result.LeadSkill);
        Assert.Equal("法說會要點蒸餾", result.LeadSkillDisplayName);
        Assert.Equal("TW", result.ContextEnvelope.Market);
        Assert.Equal("conference-call-takeaways", runs.RoutingContext?.LeadSkill);
    }

    [Fact]
    public async Task ResearchQuestion_WhenRouterChangesExplicitYearsAndQuarters_RepairsObjectiveFromOriginalQuestion()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2454", Name = "聯發科", IsActive = true });
        await db.SaveChangesAsync();
        const string question = "聯發科2026年第一季法說會提供的2026年第二季營收與毛利率指引是什麼？";
        var runs = new FakeAgentRuns();
        var route = ResearchRoute(
            "聯發科",
            "conference-call-takeaways",
            "整理聯發科2025年第一季法說會提供的2025年第二季營收與毛利率指引");
        var service = new AgentWorkflowQueryService(db, Router(route), runs);

        var result = await service.CreateAsync(userId, new(question));

        Assert.Equal($"回答使用者問題：{question}", result.Objective);
        Assert.DoesNotContain("2025", result.Objective);
        Assert.Equal(InvestmentResearchRouter.PromptVersion, runs.RoutingContext?.PromptVersion);
    }

    [Fact]
    public async Task ResearchQuestion_WhenObjectivePreservesExplicitConstraints_KeepsRouterObjective()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2454", Name = "聯發科", IsActive = true });
        await db.SaveChangesAsync();
        const string objective = "整理聯發科2026年第一季法說會提供的2026年第二季營收與毛利率指引";
        var runs = new FakeAgentRuns();
        var service = new AgentWorkflowQueryService(
            db,
            Router(ResearchRoute("聯發科", "conference-call-takeaways", objective)),
            runs);

        var result = await service.CreateAsync(userId, new("聯發科2026年第一季法說會提供的2026年第二季營收與毛利率指引是什麼？"));

        Assert.Equal(objective, result.Objective);
    }

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static InvestmentResearchRouter Router(string content) => Router(new FakeChat(content));
    private static InvestmentResearchRouter Router(FakeChat chat) => new(chat, new WorkflowSkillCatalog());
    private static string ResearchRoute(
        string? securityQuery,
        string leadSkill = "research-investigation",
        string objective = "分析使用者指定的公司研究問題") => $$"""
        {"workflowType":"ResearchInvestigation","portfolioId":null,"securityQuery":{{JsonSerializer.Serialize(securityQuery)}},"leadSkill":"{{leadSkill}}","objective":{{JsonSerializer.Serialize(objective)}},"contextEnvelope":{"market":"TW","asset":"equity","depth":"standard","horizon":null,"currency":"TWD","language":"zh-TW"},"inferredFields":[],"downstreamIntents":[],"clarifyingQuestions":[],"routingReason":"需要公司研究","confidence":"high"}
        """;
    private static string PortfolioRoute(string? horizon = null) => $$"""
        {"workflowType":"PortfolioDiagnosis","portfolioId":null,"securityQuery":null,"leadSkill":"portfolio-risk-summary","objective":"診斷投資組合風險","contextEnvelope":{"market":"TW","asset":"portfolio","depth":"standard","horizon":{{JsonSerializer.Serialize(horizon)}},"currency":"TWD","language":"zh-TW"},"inferredFields":[],"downstreamIntents":[],"clarifyingQuestions":[],"routingReason":"需要檢查投組風險","confidence":"high"}
        """;

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); }
    }

    private sealed class FakeChat(string content) : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "router-configured";
        public ChatCompletionRequest? LastRequest { get; private set; }
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatCompletionResult(content, "router-test", 10, 5));
        }
    }

    private sealed class FakeAgentRuns : IAgentRunService
    {
        public Guid? PortfolioId { get; private set; }
        public DateOnly? From { get; private set; }
        public DateOnly? To { get; private set; }
        public ResearchAskRequest? ResearchRequest { get; private set; }
        public InvestmentResearchRoutingContext? RoutingContext { get; private set; }
        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { PortfolioId = portfolioId; From = from; To = to; RoutingContext = routingContext; return Task.FromResult(Summary(AgentWorkflowTypes.PortfolioDiagnosis)); }
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { ResearchRequest = request; RoutingContext = routingContext; return Task.FromResult((Summary(AgentWorkflowTypes.ResearchInvestigation), Guid.NewGuid())); }
        private static AgentRunSummaryResponse Summary(string workflow) => new(Guid.NewGuid(), workflow, "Agent", AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0);
        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(Guid userId, Guid evidenceRemediationRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubmitAgentFeedbackResponse> SubmitFeedbackAsync(Guid runId, Guid userId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListChildrenAsync(Guid parentRunId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
