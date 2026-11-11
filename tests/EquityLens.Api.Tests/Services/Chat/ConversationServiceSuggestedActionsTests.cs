using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed partial class ConversationServiceTests
{
    private static readonly JsonSerializerOptions SuggestionJson = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly SuggestionFrom = new(2025, 1, 1), SuggestionTo = new(2025, 12, 31);

    private static WorkflowExecutionCompatibility SuggestionCompatibility() => new(
        [new PortfolioDiagnosisWorkflowDefinitionProvider(), new ResearchAgentWorkflowDefinitionProvider()], new AgentWorkflowCatalog());

    private static ConversationService SuggestionService(EquityLensDbContext db, IAgentRunService? runs = null) => new(db,
        new PortfolioAgent(null), new AgentWorkflowQueryService(db, new InvestmentResearchRouter(new CountingChat()), runs ?? new FakeAgentRuns()),
        runs ?? new FakeAgentRuns(), new AgentWorkflowCatalog(), new ConversationSuggestedActionProvider(db, SuggestionCompatibility()));

    private static async Task<(Guid User, Portfolio Portfolio, ChatSession Session, AgentRun Run)> SeedSuggestionsAsync(EquityLensDbContext db)
    {
        var user = Guid.NewGuid();
        var portfolio = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = user, Name = "高beta", BaseCurrency = "TWD" };
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(user, portfolio.Id, SuggestionFrom, SuggestionTo);
        run.Status = AgentRunStatuses.Succeeded;
        run.OutputJson = """{"summary":"已驗證結果","evidenceStatus":"complete"}""";
        var board = JsonNode.Parse(run.BlackboardJson)!.AsObject();
        board["portfolioContext"] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(portfolio.Id, portfolio.Name, "TWD", SuggestionFrom, SuggestionTo, 2), SuggestionJson);
        board["portfolioQualityGate"] = JsonNode.Parse("""{"passed":true,"limited":false,"blockingIssues":[]}""");
        board["mathResults"] = JsonNode.Parse("""[{"operation":"calculate-annualized-volatility","value":0.3}]""");
        run.BlackboardJson = board.ToJsonString();
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = user, ContextJson = new JsonObject {
            ["lastAgentRunId"] = run.Id.ToString(), ["activePortfolioId"] = portfolio.Id.ToString() }.ToJsonString() };
        db.Portfolios.Add(portfolio); db.AgentRuns.Add(run); db.ChatSessions.Add(session);
        db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = session.Id, AgentRunId = run.Id,
            Role = "assistant", MessageType = "AgentRun", Content = "分析完成", SequenceNumber = 1 });
        await db.SaveChangesAsync();
        return (user, portfolio, session, run);
    }

    private static void ChangeBoard(AgentRun run, Action<JsonObject> change)
    {
        var board = JsonNode.Parse(run.BlackboardJson)!.AsObject(); change(board); run.BlackboardJson = board.ToJsonString();
    }

    private static JsonNode Comparison(Guid portfolioId)
    {
        var metrics = new ScenarioMetrics(.2m, .03m, .04m, .15m, []);
        var baseline = new PortfolioScenarioResult("原組合", "Calculated", new Dictionary<string, decimal>(), new Dictionary<string, decimal>(), 0, .5m, .5m, metrics, null);
        var snapshot = new PortfolioScenarioSnapshot(portfolioId, DateTime.UtcNow, "TWD", SuggestionFrom, SuggestionTo, [], [], 0, []);
        return JsonSerializer.SerializeToNode(new PortfolioScenarioComparison("Calculated", "test", "hash", snapshot, 252, .95m, 1,
            baseline, [baseline with { Name = "減碼留現金" }], []), SuggestionJson)!;
    }

    [Fact]
    public async Task PortfolioRunCard_CarriesKeyMetricsFromToolOutput()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        seed.Run.OutputJson = """{"summary":"x","riskMetrics":{"annualizedVolatility":0.5339,"maxDrawdown":-0.3166,"historicalVaR":null,"largestWeight":0.7692,"concentrationHhi":0.645},"portfolioReturn":1.1823,"benchmarkReturn":0.6211}""";
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        var metrics = card!.KeyMetrics!;
        Assert.Equal(.5339m, metrics.AnnualizedVolatility);
        Assert.Equal(-.3166m, metrics.MaxDrawdown);
        Assert.Null(metrics.HistoricalVaR);
        Assert.Equal(.6211m, metrics.BenchmarkReturn);
    }

    [Fact]
    public async Task SuggestedActions_RiskOverviewUsesVerifiedNameAndNoModelCallsOrWrites()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        var service = SuggestionService(db);
        var count = await db.AgentRuns.CountAsync();
        var card = await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "portfolio-reduce-volatility", "portfolio-risk-contributors", "portfolio-stress-test" }, card!.SuggestedActions!.Select(x => x.Id));
        Assert.All(card.SuggestedActions!, action => {
            Assert.Contains(seed.Portfolio.Name, action.Prompt); Assert.Contains(seed.Portfolio.Name, action.PromptEn!);
            Assert.DoesNotContain("ETF", action.Prompt); Assert.DoesNotContain("0050", action.Prompt);
        });
        Assert.Equal(count, await db.AgentRuns.CountAsync());
        Assert.False(db.ChangeTracker.HasChanges());
        db.ChangeTracker.Clear();
        var reloaded = await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(card.SuggestedActions!.ToArray(), reloaded!.SuggestedActions!.ToArray());
    }

    [Theory]
    [InlineData("math")]
    [InlineData("tool")]
    [InlineData("report")]
    public async Task SuggestedActions_ExistingRiskContributionIsNotRecommendedAgain(string location)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        var contribution = JsonNode.Parse("""[{"operation":"calculate-volatility-risk-contribution","value":{"holdings":[{"ticker":"2330","weight":0.5,"componentVolatility":0.2,"componentRiskShare":0.6}]}}]""")!;
        ChangeBoard(seed.Run, board => {
            if (location == "math") ((JsonArray)board["mathResults"]!).Add(contribution[0]!.DeepClone());
            else board["agentLoopFacts"] = new JsonArray(new JsonObject {
                ["tool"] = location == "tool" ? "run_risk_analysis" : "get_risk_report",
                ["data"] = location == "tool" ? new JsonObject { ["results"] = contribution.DeepClone() }
                    : new JsonObject { ["metrics"] = new JsonObject { ["contributions"] = contribution[0]!["value"]!["holdings"]!.DeepClone() } } });
        });
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "portfolio-reduce-volatility", "portfolio-stress-test", "portfolio-benchmark" }, card!.SuggestedActions!.Select(x => x.Id));
    }

    [Fact]
    public async Task SuggestedActions_CoveredStressBenchmarkTailRiskAndProfileAreNotRecommendedAgain()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => {
            ((JsonArray)board["mathResults"]!).Add(JsonNode.Parse("""{"operation":"calculate-historical-var","value":0.03}"""));
            board["agentLoopFacts"] = new JsonArray(
                new JsonObject { ["tool"] = "get_performance_attribution", ["data"] = new JsonObject() },
                new JsonObject { ["tool"] = "run_stress_test", ["data"] = new JsonObject() },
                new JsonObject { ["tool"] = "get_risk_profile", ["data"] = new JsonObject() });
        });
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "portfolio-reduce-volatility", "portfolio-risk-contributors" }, card!.SuggestedActions!.Select(x => x.Id));
    }

    [Fact]
    public async Task SuggestedActions_LaterTopicsFillFreedSlotsInPriorityOrder()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => board["agentLoopFacts"] = new JsonArray(
            new JsonObject { ["tool"] = "run_stress_test", ["data"] = new JsonObject() },
            new JsonObject { ["tool"] = "get_performance_attribution", ["data"] = new JsonObject() }));
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "portfolio-reduce-volatility", "portfolio-risk-contributors", "portfolio-tail-risk" }, card!.SuggestedActions!.Select(x => x.Id));
        Assert.All(card.SuggestedActions!, action => Assert.DoesNotMatch(@"20\d{2}", action.Prompt));
    }

    [Theory]
    [InlineData("Calculated", new[] { "portfolio-risk-contributors", "portfolio-benchmark", "portfolio-tail-risk" })]
    [InlineData("Unavailable", new string[0])]
    public async Task SuggestedActions_StressOnlyAnswerWithoutVolatility_SuggestsOnlyWhenStressWasCalculated(string status, string[] expected)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => {
            board["mathResults"] = new JsonArray();
            board["agentLoopFacts"] = new JsonArray(new JsonObject { ["tool"] = "run_stress_test", ["data"] = new JsonObject { ["status"] = status } });
        });
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(expected, card!.SuggestedActions!.Select(x => x.Id));
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData("complete", false, true)]
    [InlineData("partial", false, false)]
    [InlineData(null, true, false)]
    public async Task SuggestedActions_StressAnswerThroughRealGate_RequiresValidAnswerAndAvailableCoverage(
        string? attributionCoverage, bool invalidAnswer, bool expectedSuggestions)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => {
            board["mathResults"] = new JsonArray();
            board["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(
                new LoopAnswer(invalidAnswer ? "壓力情境虧 99% [P1]。" : "壓力情境虧 25% [P1]。", ["P1"]), SuggestionJson);
            board["agentLoopFacts"] = JsonSerializer.SerializeToNode(new[] {
                new LoopDataFact("P1", "stress", "run_stress_test", new JsonObject { ["status"] = "Calculated", ["totalImpact"] = -0.25m }) }, SuggestionJson);
            if (attributionCoverage is not null)
                board[AgentBlackboardKeys.PerformanceAttribution] = JsonSerializer.SerializeToNode(
                    new PortfolioPerformanceAttribution(0, 0, 0, 2, 2, [], [], attributionCoverage), SuggestionJson);
        });
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, seed.Run, new AgentRunNode(), (_, _, _, _, _) => { }));
        var board = JsonNode.Parse(seed.Run.BlackboardJson)!;
        seed.Run.OutputJson = board[AgentBlackboardKeys.PortfolioDiagnosisDraft]!.ToJsonString();
        Assert.Equal(expectedSuggestions, !board["portfolioQualityGate"]!["limited"]!.GetValue<bool>());
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        if (expectedSuggestions)
            Assert.Equal(new[] { "portfolio-risk-contributors", "portfolio-benchmark", "portfolio-tail-risk" }, card!.SuggestedActions!.Select(x => x.Id));
        else Assert.Empty(card!.SuggestedActions!);
    }

    [Theory]
    [InlineData("math")]
    [InlineData("report")]
    public async Task SuggestedActions_TailRiskOnlyAnswer_OffersOtherTopicsWithoutVolatility(string location)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => {
            board["mathResults"] = location == "math"
                ? JsonNode.Parse("""[{"operation":"calculate-historical-var","value":-0.027},{"operation":"calculate-expected-shortfall","value":-0.037}]""")
                : new JsonArray();
            if (location == "report")
                board["agentLoopFacts"] = JsonNode.Parse("""[{"tool":"get_risk_report","data":{"metrics":{"historicalVaR":-0.027,"expectedShortfall":-0.037}}}]""");
        });
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "portfolio-risk-contributors", "portfolio-stress-test", "portfolio-benchmark" }, card!.SuggestedActions!.Select(x => x.Id));
    }

    [Fact]
    public async Task SuggestedActions_TailRiskFromSavedReportMetricsIsNotRecommendedAgain()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => board["agentLoopFacts"] = new JsonArray(new JsonObject {
            ["tool"] = "get_risk_report", ["data"] = new JsonObject { ["metrics"] = new JsonObject { ["expectedShortfall"] = 0.05 } } }));
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.DoesNotContain(card!.SuggestedActions!, x => x.Id == "portfolio-tail-risk");
    }

    [Fact]
    public async Task SuggestedActions_CompletedScenarioReplacesOptimizationQuestions()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => board[PortfolioScenarioCalculator.ResultKey] = Comparison(seed.Portfolio.Id));
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "scenario-remaining-risks", "scenario-metrics", "scenario-profile-fit" }, card!.SuggestedActions!.Select(x => x.Id));
        Assert.All(card.SuggestedActions!, action => Assert.Contains(seed.Portfolio.Name, action.Prompt));
    }

    [Fact]
    public async Task SuggestedActions_ScenarioOmitsProfileFitWhenProfileWasAlreadyUsed()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, board => {
            board[PortfolioScenarioCalculator.ResultKey] = Comparison(seed.Portfolio.Id);
            board["agentLoopFacts"] = new JsonArray(new JsonObject { ["tool"] = "get_risk_profile", ["data"] = new JsonObject() });
        });
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Equal(new[] { "scenario-remaining-risks", "scenario-metrics" }, card!.SuggestedActions!.Select(x => x.Id));
    }

    [Theory]
    [InlineData("Pending")][InlineData("Running")][InlineData("WaitingForApproval")][InlineData("WaitingForFeedback")]
    [InlineData("Failed")][InlineData("Cancelled")]
    [InlineData("refused")][InlineData("limited")][InlineData("missing-gate")][InlineData("missing-data")]
    [InlineData("missing-target")][InlineData("foreign-target")][InlineData("duplicate-name")]
    [InlineData("old-version")][InlineData("old-node")][InlineData("partial-scenario")][InlineData("wrong-scenario-target")]
    public async Task SuggestedActions_IneligibleRunsOfferNothing(string condition)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        switch (condition)
        {
            case "refused": ChangeBoard(seed.Run, b => b["portfolioQualityGate"]!["passed"] = false); break;
            case "limited": ChangeBoard(seed.Run, b => b["portfolioQualityGate"]!["limited"] = true); break;
            case "missing-gate": ChangeBoard(seed.Run, b => b.Remove("portfolioQualityGate")); break;
            case "missing-data": ChangeBoard(seed.Run, b => b.Remove("mathResults")); break;
            case "missing-target": seed.Portfolio.IsActive = false; break;
            case "foreign-target": seed.Portfolio.OwnerUserId = Guid.NewGuid(); break;
            case "duplicate-name": db.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), OwnerUserId = seed.User, Name = seed.Portfolio.Name }); break;
            case "old-version": seed.Run.WorkflowDefinitionJson = seed.Run.WorkflowDefinitionJson.Replace($"\"version\":{PortfolioDiagnosisWorkflow.Version}", "\"version\":1"); break;
            case "old-node": seed.Run.Nodes.First().NodeType = "RetiredNode"; break;
            case "partial-scenario": ChangeBoard(seed.Run, b => { b[PortfolioScenarioCalculator.ResultKey] = Comparison(seed.Portfolio.Id); b[PortfolioScenarioCalculator.ResultKey]!["scenarioErrors"] = new JsonArray("unavailable"); }); break;
            case "wrong-scenario-target": ChangeBoard(seed.Run, b => b[PortfolioScenarioCalculator.ResultKey] = Comparison(Guid.NewGuid())); break;
            default: seed.Run.Status = condition; break;
        }
        await db.SaveChangesAsync();
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        Assert.Empty(card!.SuggestedActions!);
    }

    [Fact]
    public async Task SuggestedActions_LatestMessageAndResetRemainSuppressedAfterReload()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        var service = SuggestionService(db);
        Assert.NotEmpty((await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id))!.SuggestedActions!);
        Assert.Null(await service.GetRunCardAsync(Guid.NewGuid(), seed.Session.Id, seed.Run.Id));
        Assert.Null(await service.GetRunCardAsync(seed.User, Guid.NewGuid(), seed.Run.Id));
        db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = seed.Session.Id, Role = "user", Content = "後續問題", SequenceNumber = 2 });
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id))!.SuggestedActions!);
        await service.ProcessAsync(seed.User, seed.Session.Id, new("重置對話", Guid.NewGuid()));
        db.ChangeTracker.Clear();
        Assert.Empty((await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id))!.SuggestedActions!);
    }

    [Theory]
    [InlineData("valid")][InlineData("refused")][InlineData("no-evidence")][InlineData("foreign-research")][InlineData("ambiguous-ticker")]
    public async Task SuggestedActions_ResearchRequiresAcceptedEvidenceAndUniqueOwnedSubject(string condition)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        var research = new ResearchRun { Id = Guid.NewGuid(), UserId = seed.User, TraceId = "trace", Ticker = "2330", Question = "營運風險", Answer = "來源支持的答案", CitationCount = 1 };
        db.ResearchRuns.Add(research);
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2330", Name = "台積電", AssetType = "Stock" });
        seed.Run.WorkflowType = AgentWorkflowTypes.ResearchAgent;
        seed.Run.WorkflowDefinitionJson = new ResearchAgentWorkflowDefinitionProvider().CreateRun(seed.User, research.Id).WorkflowDefinitionJson;
        seed.Run.Nodes.Clear();
        seed.Run.Nodes = new ResearchAgentWorkflowDefinitionProvider().CreateRun(seed.User, research.Id).Nodes;
        foreach (var node in seed.Run.Nodes) node.AgentRunId = seed.Run.Id;
        db.AgentRunNodes.AddRange(seed.Run.Nodes);
        seed.Run.ResearchRunId = research.Id;
        seed.Run.OutputJson = """{"answer":"來源支持的答案","status":"Answered","outputGate":{"passed":true},"criticReview":{"recommendedNextAction":"AcceptAnswer"}}""";
        if (condition == "refused") seed.Run.OutputJson = """{"answer":"證據不足","errorCode":"quality_gate_rejected"}""";
        if (condition == "no-evidence") research.CitationCount = 0;
        if (condition == "foreign-research") research.UserId = Guid.NewGuid();
        if (condition == "ambiguous-ticker") db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2330", Exchange = "other" });
        await db.SaveChangesAsync();
        var chat = new SuggestionChat("""{"ids":["research-evidence","research-operating-risks"]}""");
        var provider = new ConversationSuggestedActionProvider(db, SuggestionCompatibility(), new(chat));
        await provider.PrepareAsync(new(db, seed.Run, seed.Run.Nodes.Last(), (_, _, _, _, _) => {}), research.Answer!);
        Assert.Equal(condition == "valid" ? 1 : 0, chat.Calls);
        var card = await SuggestionService(db).GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id);
        if (condition != "valid") Assert.Empty(card!.SuggestedActions!);
        else {
            Assert.Equal(new[] { "research-evidence", "research-operating-risks" }, card!.SuggestedActions!.Select(a => a.Id));
            Assert.All(card.SuggestedActions!, a => { Assert.Contains("2330", a.Prompt); Assert.Contains("2330", a.PromptEn!); });
        }
    }

    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)][InlineData(true, true)]
    public async Task SuggestedAction_SubmitAndEditedTargetUseRealCreationAndOwnershipGuards(bool switchPortfolio, bool english)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        var other = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = seed.User, Name = "退休" };
        db.Portfolios.Add(other); await db.SaveChangesAsync();
        var queue = new SuggestionQueue();
        var provider = new PortfolioDiagnosisWorkflowDefinitionProvider();
        var runs = new AgentRunService(db, [provider, new ResearchAgentWorkflowDefinitionProvider()], new AgentRunStateMachine(), new AgentNodeStateMachine(), queue,
            portfolioDiagnosisProvider: provider, compatibility: SuggestionCompatibility());
        var service = SuggestionService(db, runs);
        var actions = (await service.GetRunCardAsync(seed.User, seed.Session.Id, seed.Run.Id))!.SuggestedActions!;
        var prompt = english ? actions[0].PromptEn! : actions[0].Prompt;
        if (switchPortfolio) prompt = prompt.Replace(seed.Portfolio.Name, other.Name);
        Assert.Empty(queue.Messages);
        var outcome = await service.ProcessAsync(seed.User, seed.Session.Id, new(prompt, Guid.NewGuid()));
        Assert.Equal(ConversationActions.RouteWorkflow, outcome.Action);
        var queued = Assert.Single(queue.Messages);
        var created = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == queued.RunId);
        Assert.Equal(switchPortfolio ? other.Id : seed.Portfolio.Id, PortfolioDiagnosisWorkflowDefinitionProvider.ParseInput(created.InputJson).PortfolioId);
        Assert.Equal(PortfolioDiagnosisWorkflowDefinitionProvider.Steps.Select(s => s.Type).Order(), created.Nodes.Select(n => n.NodeType).Order());
        Assert.Equal(seed.User, created.UserId);
        var foreign = Guid.NewGuid();
        var rejected = await service.ProcessAsync(seed.User, seed.Session.Id, new($"分析投資組合 {foreign} 的波動率", Guid.NewGuid()));
        Assert.Equal(ConversationActions.RejectUnsafe, rejected.Action);
        Assert.Single(queue.Messages);
    }

    private sealed class SuggestionQueue : IAgentRunQueue
    {
        public List<AgentRunQueueMessage> Messages { get; } = [];
        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunQueueItem?> ReadStalePendingAsync(string consumerName, TimeSpan minIdleTime, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
