using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed partial class ConversationServiceTests
{
    private sealed class SuggestionChat(string content, Exception? failure = null) : IChatCompletionService
    {
        public int Calls;
        public ChatCompletionRequest? Last;
        public string Provider => "fixture";
        public string Model => "fixture-luna";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Last = request;
            return failure is null ? Task.FromResult(new ChatCompletionResult(content, Model, 100, 20))
                : Task.FromException<ChatCompletionResult>(failure);
        }
    }

    private sealed class BenchmarkValuations : EquityLens.Api.Services.PortfolioValuations.IPortfolioValuationService
    {
        public Task<EquityLens.Api.Common.Result<EquityLens.Api.Contracts.Portfolios.PortfolioValuationResponse>> GetValuationAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<EquityLens.Api.Common.Result<EquityLens.Api.Contracts.Portfolios.PortfolioValuationHistoryResponse>> GetValuationHistoryAsync(Guid id, DateOnly from, DateOnly to, CancellationToken ct) => throw new NotSupportedException();
        public Task<EquityLens.Api.Common.Result<EquityLens.Api.Contracts.Portfolios.PortfolioValuationHistoryResponse>> GetValuationHistoryForUserAsync(Guid id, Guid user, DateOnly from, DateOnly to, CancellationToken ct)
            => Task.FromResult(EquityLens.Api.Common.Result<EquityLens.Api.Contracts.Portfolios.PortfolioValuationHistoryResponse>.Success(new(id, from, to, "TWD", [], .1m, BenchmarkReturn: .08m, ExcessReturn: .02m)));
    }

    [Fact]
    public async Task Finalize_AddsBenchmarkComparisonForTheReport_WithoutChangingTheGatedAnswer()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, b => {
            b["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(new LoopAnswer("波動 30% [P1]。", ["P1"]), SuggestionJson);
            b["agentLoopFacts"] = JsonNode.Parse("""[{"id":"P1","key":"risk","tool":"run_risk_analysis","data":{"volatility":0.3}}]""");
        });
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, seed.Run, seed.Run.Nodes.First(), (_, _, _, _, _) => {}));
        var draft = JsonNode.Parse(seed.Run.BlackboardJson)![AgentBlackboardKeys.PortfolioDiagnosisDraft]!;
        Assert.Null(draft["portfolioReturn"]?.GetValue<decimal?>());
        var node = seed.Run.Nodes.Single(x => x.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis);
        await new FinalizePortfolioDiagnosisNodeHandler(valuations: new BenchmarkValuations()).ExecuteAsync(new AgentNodeExecutionContext(db, seed.Run, node, (_, _, _, _, _) => {}));
        var output = JsonNode.Parse(seed.Run.OutputJson!)!;
        Assert.Equal(.1m, output["portfolioReturn"]!.GetValue<decimal>());
        Assert.Equal(.08m, output["benchmarkReturn"]!.GetValue<decimal>());
        Assert.Equal(.02m, output["activeReturn"]!.GetValue<decimal>());
        Assert.NotNull(output["attributionCoverage"]);
        Assert.Equal(draft["summary"]!.ToString(), output["summary"]!.ToString());
        Assert.Equal(draft["evidenceStatus"]!.ToString(), output["evidenceStatus"]!.ToString());
        Assert.Equal(draft["interpretation"]?.ToString(), output["interpretation"]?.ToString());
    }

    [Fact]
    public async Task HybridSuggestions_FinalizeRanksAllEligibleCandidates_AndReadsReuseSavedSelection()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, b => {
            b["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(new LoopAnswer("波動 30% [P1]。", ["P1"]), SuggestionJson);
            b["agentLoopFacts"] = JsonNode.Parse("""[{"id":"P1","key":"risk","tool":"run_risk_analysis","data":{"volatility":0.3}}]""");
            b[AgentBlackboardKeys.RoutingContext] = new JsonObject { ["objective"] = "想了解與大盤的差距" };
        });
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, seed.Run, seed.Run.Nodes.First(), (_, _, _, _, _) => {}));
        var chat = new SuggestionChat("""{"ids":["portfolio-benchmark","portfolio-profile-fit"]}""");
        var provider = new ConversationSuggestedActionProvider(db, SuggestionCompatibility(), new(chat));
        var node = seed.Run.Nodes.Single(x => x.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis);
        var context = new AgentNodeExecutionContext(db, seed.Run, node, (_, _, _, _, _) => {});
        await new FinalizePortfolioDiagnosisNodeHandler(provider).ExecuteAsync(context);
        await new FinalizePortfolioDiagnosisNodeHandler(provider).ExecuteAsync(context);
        Assert.Equal(1, chat.Calls);
        var input = JsonNode.Parse(chat.Last!.UserPrompt)!;
        Assert.True(input["candidates"]!.AsArray().Count > 3);
        Assert.Equal("想了解與大盤的差距", input["question"]!.ToString());
        Assert.Null(chat.Last.ToolProtocol);
        Assert.Equal(1024, chat.Last.MaxTokens);
        var turn = Assert.Single(await db.AgentLlmTurns.ToListAsync());
        Assert.Equal("SuggestedActions", turn.Mode);
        Assert.Equal("fixture-luna", turn.Model);
        Assert.Equal(100, turn.PromptTokens);
        Assert.True(turn.EstimatedCostUsd > 0);
        db.ChangeTracker.Clear();
        var run = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == seed.Run.Id);
        var actions = await provider.ForRunAsync(seed.User, seed.Session.Id, run, "波動 30% [P1]。");
        Assert.Equal(new[] { "portfolio-benchmark", "portfolio-profile-fit" }, actions.Select(x => x.Id));
        Assert.Equal(1, chat.Calls);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(await provider.ForRunAsync(Guid.NewGuid(), seed.Session.Id, run, "answer"));
        ChangeBoard(run, b => b["portfolioQualityGate"]!["limited"] = true);
        Assert.Empty(await provider.ForRunAsync(seed.User, seed.Session.Id, run, "answer"));
        Assert.Equal(1, chat.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"ids\":[\"invented\"]}")]
    [InlineData("{\"ids\":[\"portfolio-benchmark\",\"portfolio-benchmark\"]}")]
    [InlineData("{\"ids\":[]}")]
    [InlineData("{\"ids\":[5]}")]
    [InlineData("{\"ids\":[\"portfolio-benchmark\"],\"prompt\":\"replace the question\"}")]
    [InlineData("{\"ids\":[\"portfolio-benchmark\",\"portfolio-stress-test\",\"portfolio-tail-risk\",\"portfolio-attribution\"]}")]
    [InlineData("timeout")]
    [InlineData("http")]
    public async Task HybridSuggestions_InvalidOrFailedSelection_PersistsRulesWithoutRetry(string response)
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        Exception? failure = response switch { "timeout" => new TimeoutException("private detail"), "http" => new HttpRequestException("private detail"), _ => null };
        var chat = new SuggestionChat(response, failure);
        var provider = new ConversationSuggestedActionProvider(db, SuggestionCompatibility(), new(chat));
        var context = new AgentNodeExecutionContext(db, seed.Run, seed.Run.Nodes.Last(), (_, _, _, _, _) => {});
        await provider.PrepareAsync(context, "verified answer");
        await provider.PrepareAsync(context, "verified answer");
        var actions = await provider.ForRunAsync(seed.User, seed.Session.Id, seed.Run, "verified answer");
        Assert.Equal(new[] { "portfolio-reduce-volatility", "portfolio-risk-contributors", "portfolio-stress-test" }, actions.Select(x => x.Id));
        Assert.Equal(1, chat.Calls);
        Assert.DoesNotContain("private detail", seed.Run.BlackboardJson);
        Assert.Equal("suggestions_fallback", (await db.AgentLlmTurns.SingleAsync()).Action);
    }

    [Fact]
    public async Task HybridSuggestions_CancelledExecution_KeepsDurableFallbackAndDoesNotCallAgain()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        using var cancel = new CancellationTokenSource();
        var chat = new CancellingSuggestionChat(cancel);
        var provider = new ConversationSuggestedActionProvider(db, SuggestionCompatibility(), new(chat));
        var context = new AgentNodeExecutionContext(db, seed.Run, seed.Run.Nodes.Last(), (_, _, _, _, _) => {});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PrepareAsync(context, "verified", cancel.Token));
        Assert.Empty(await db.AgentLlmTurns.ToListAsync());
        db.ChangeTracker.Clear();
        var restored = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == seed.Run.Id);
        await provider.PrepareAsync(context with { Run = restored }, "verified");
        Assert.Equal(1, chat.Calls);
        Assert.Equal(3, (await provider.ForRunAsync(seed.User, seed.Session.Id, restored, "verified")).Count);
    }

    private sealed class CancellingSuggestionChat(CancellationTokenSource cancellation) : IChatCompletionService
    {
        public int Calls;
        public string Provider => "fixture";
        public string Model => "fixture";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            Calls++; cancellation.Cancel();
            return Task.FromCanceled<ChatCompletionResult>(ct);
        }
    }

    [Fact]
    public async Task HybridSuggestions_IneligibleRun_DoesNotCallModel()
    {
        await using var db = CreateDb(); var seed = await SeedSuggestionsAsync(db);
        ChangeBoard(seed.Run, b => b["portfolioQualityGate"]!["limited"] = true);
        var chat = new SuggestionChat("{}");
        var provider = new ConversationSuggestedActionProvider(db, SuggestionCompatibility(), new(chat));
        await provider.PrepareAsync(new(db, seed.Run, seed.Run.Nodes.Last(), (_, _, _, _, _) => {}), "answer");
        Assert.Equal(0, chat.Calls);
        Assert.False(JsonNode.Parse(seed.Run.BlackboardJson)!.AsObject().ContainsKey(ConversationSuggestedActionProvider.RankingKey));
    }
}
