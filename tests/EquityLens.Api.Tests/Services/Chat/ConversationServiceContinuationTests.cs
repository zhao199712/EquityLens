using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed partial class ConversationServiceTests
{
    private sealed class RecordingAgent(Func<ConversationAgentInput, ConversationAgentDecision> decide) : IConversationAgent
    {
        public List<ConversationAgentInput> Inputs { get; } = [];
        public Task<ConversationAgentDecision> DecideAsync(ConversationAgentInput input, CancellationToken cancellationToken = default)
        { Inputs.Add(input); return Task.FromResult(decide(input)); }
    }
    private static ConversationAgentDecision Route(ConversationAgentInput input, string workflow) =>
        new(ConversationActions.RouteWorkflow, "分析", input.Message, "test", "high", new(), "", "test", "test", 0, 0, 0,
            InvestmentResearchRouter.Fixed(workflow, input.Message));

    [Fact]
    public async Task MissingPortfolio_SelectionRestoresQuestionAndClearsPendingTask()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "組合甲" };
        db.AddRange(session, portfolio, new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "組合乙" }); await db.SaveChangesAsync();
        var runs = new FakeAgentRuns(); var agent = new RecordingAgent(i => Route(i, AgentWorkflowTypes.PortfolioDiagnosis));
        var service = new ConversationService(db, agent, new AgentWorkflowQueryService(db, new InvestmentResearchRouter(new CountingChat()), runs), runs, new AgentWorkflowCatalog());
        var first = await service.ProcessAsync(user, session.Id, new("與大盤比較最近一年績效", Guid.NewGuid()));
        Assert.Equal(ConversationActions.AskClarification, first.Action);
        var second = await service.ProcessAsync(user, session.Id, new("組合甲", Guid.NewGuid()));
        Assert.Equal(ConversationActions.RouteWorkflow, second.Action);
        Assert.Equal(portfolio.Id, runs.PortfolioId);
        Assert.Contains("與大盤比較最近一年績效", agent.Inputs.Last().Message);
        Assert.Contains("組合甲", agent.Inputs.Last().Message);
        Assert.Null(JsonNode.Parse(session.ContextJson)!["pendingTask"]);
    }

    [Theory]
    [InlineData("接下來看組合甲。")]
    [InlineData("改用組合甲")]
    public async Task SelectWithoutPendingTask_DoesNotStartAnalysis(string text)
    {
        await using var db = CreateDb(); var user = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user };
        db.AddRange(session, new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "組合甲" }); await db.SaveChangesAsync();
        var agent = new RecordingAgent(i => throw new InvalidOperationException("Selection must be handled locally"));
        var queries = new FakeWorkflowQueries();
        var service = new ConversationService(db, agent, queries, new FakeAgentRuns(), new AgentWorkflowCatalog());
        var outcome = await service.ProcessAsync(user, session.Id, new(text, Guid.NewGuid()));
        Assert.Equal(ConversationActions.DirectResponse, outcome.Action);
        Assert.Empty(agent.Inputs); Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task ResearchThenPortfolio_ClassifiesCurrentGoalAndLinksSourceWithoutForcingResearchWorkflow()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var sourceId = Guid.NewGuid(); var artifactId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = new JsonObject { ["lastAgentRunId"] = sourceId }.ToJsonString() };
        db.AddRange(session, new AgentRun { Id = sourceId, UserId = user, WorkflowType = AgentWorkflowTypes.ResearchAgent, Status = AgentRunStatuses.Succeeded, ResearchRunId = artifactId },
            new ResearchRun { Id = artifactId, UserId = user, Ticker = "2317", Question = "鴻海營收組合", Answer = "研究內容", TraceId = "t", RetrievalMode = "Auto", SourcePolicy = "Auto" });
        await db.SaveChangesAsync();
        var agent = new RecordingAgent(i => Route(i, AgentWorkflowTypes.PortfolioDiagnosis)); var queries = new FakeWorkflowQueries();
        var service = new ConversationService(db, agent, queries, new FakeAgentRuns(), new AgentWorkflowCatalog());
        await service.ProcessAsync(user, session.Id, new("接著看怎麼降低我的投組波動", Guid.NewGuid()));
        Assert.Single(agent.Inputs); Assert.Equal("2317", agent.Inputs[0].Context["recentResearch"]!["ticker"]!.GetValue<string>());
        Assert.Equal(AgentWorkflowTypes.PortfolioDiagnosis, queries.Decision!.WorkflowType);
        Assert.Null(queries.ParentId); Assert.Equal(sourceId, queries.Decision.RoutingContext.ResearchContextRunId);
    }

    [Fact]
    public async Task ScenarioFollowup_ReceivesConfirmedWeightsAndOwnedTickerMapping()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid(); var parentId = Guid.NewGuid(); var portfolioId = Guid.NewGuid();
        var routing = InvestmentResearchRouter.Fixed(AgentWorkflowTypes.PortfolioDiagnosis, "台積電降至40%，其餘留現金").RoutingContext with {
            Scenarios = [new("減碼", new Dictionary<string, decimal> { ["2330"] = .4m })] };
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = new JsonObject { ["activePortfolioId"] = portfolioId, ["lastAgentRunId"] = parentId }.ToJsonString() };
        var portfolio = new Portfolio { Id = portfolioId, OwnerUserId = user, Name = "組合甲" };
        var security = new Security { Id = Guid.NewGuid(), Ticker = "2330", Name = "台積電", IsActive = true };
        db.AddRange(session, portfolio, security, new PortfolioHolding { Id = Guid.NewGuid(), Portfolio = portfolio, PortfolioId = portfolioId, Security = security, SecurityId = security.Id, Quantity = 10 },
            new AgentRun { Id = parentId, UserId = user, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis, Status = AgentRunStatuses.Succeeded,
                InputJson = AgentNodeJson.Serialize(new { portfolioId, routingContext = routing }) });
        await db.SaveChangesAsync();
        var agent = new RecordingAgent(i => Route(i, AgentWorkflowTypes.PortfolioDiagnosis));
        var service = new ConversationService(db, agent, new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());
        await service.ProcessAsync(user, session.Id, new("那改成轉入0050", Guid.NewGuid()));
        var context = agent.Inputs.Single().Context;
        Assert.Equal(.4m, context["previousScenarios"]![0]!["targetWeights"]!["2330"]!.GetValue<decimal>());
        Assert.Equal("台積電", context["portfolioSecurities"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("2330", context["tickerCandidates"]!.ToJsonString());
    }

    [Fact]
    public async Task Reset_CutsOffOldMessagesPendingQuestionAndResearchContext()
    {
        await using var db = CreateDb(); var user = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = """{"pendingTask":{"question":"舊目標"},"lastTask":{"question":"舊任務"}}""" };
        db.AddRange(session, new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = session.Id, Role = "user", Content = "舊目標台積電", SequenceNumber = 0 }); await db.SaveChangesAsync();
        var agent = new RecordingAgent(i => new(ConversationActions.DirectResponse, "新對話", null, "test", "high", new(), "", "test", "test", 0, 0, 0));
        var service = new ConversationService(db, agent, new FakeWorkflowQueries(), new FakeAgentRuns(), new AgentWorkflowCatalog());
        await service.ProcessAsync(user, session.Id, new("重置對話", Guid.NewGuid()));
        await service.ProcessAsync(user, session.Id, new("你好", Guid.NewGuid()));
        var input = agent.Inputs.Single();
        Assert.DoesNotContain(input.History, x => x.Content?.Contains("舊目標") == true);
        Assert.Null(input.Context["pendingTask"]); Assert.Null(input.Context["lastTask"]); Assert.Null(input.Context["researchContextRunId"]);
    }
}
