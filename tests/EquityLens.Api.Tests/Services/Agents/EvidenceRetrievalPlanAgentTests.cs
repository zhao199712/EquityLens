using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class EvidenceRetrievalPlanAgentTests
{
    [Fact]
    public async Task PlanAsync_ValidStructuredResponse_UsesLlmPlan()
    {
        var chat = new FakeChat(["{\"searches\":[{\"documentType\":\"AnnualReport\",\"sourceRole\":\"Primary\",\"query\":\"海外設廠成本\",\"topK\":6,\"reason\":\"補足成本證據\",\"targetClaim\":\"海外設廠風險\",\"freshness\":null}]}"]);
        var result = await new EvidenceRetrievalPlanAgent(chat, new FakeFallback()).PlanAsync(Input());
        Assert.Equal("Llm", result.Mode); Assert.Equal("海外設廠成本", Assert.Single(result.Plan.Searches).Query); Assert.Equal(6, result.Plan.Searches[0].TopK);
    }

    [Fact]
    public async Task PlanAsync_FirstInvalidResponse_RepairsOnce()
    {
        var chat = new FakeChat(["{\"searches\":[]}", "{\"searches\":[{\"documentType\":null,\"sourceRole\":\"Primary\",\"query\":\"供應鏈風險\",\"topK\":5,\"reason\":\"repair\",\"targetClaim\":\"海外設廠風險\",\"freshness\":\"year\"}]}"]);
        var result = await new EvidenceRetrievalPlanAgent(chat, new FakeFallback()).PlanAsync(Input());
        Assert.Equal("LlmRepair", result.Mode); Assert.Equal(2, chat.CallCount);
    }

    [Fact]
    public async Task PlanAsync_InvalidAndDuplicateResponses_UsesDeterministicFallback()
    {
        var chat = new FakeChat(["not-json", "{\"searches\":[{\"documentType\":null,\"sourceRole\":\"Primary\",\"query\":\"already used\",\"topK\":20,\"reason\":\"invalid\",\"targetClaim\":\"海外設廠風險\",\"freshness\":null}]}"]);
        var result = await new EvidenceRetrievalPlanAgent(chat, new FakeFallback()).PlanAsync(Input() with { PreviousQueries = ["already used"] });
        Assert.Equal("DeterministicFallback", result.Mode); Assert.Equal("fallback query", Assert.Single(result.Plan.Searches).Query); Assert.NotNull(result.FallbackReason);
    }

    private static EvidenceRetrievalPlanningInput Input() => new("主要營運風險？", ["海外設廠風險"], [], [], 1);

    private sealed class FakeChat(Queue<string> responses) : IChatCompletionService
    {
        public FakeChat(IEnumerable<string> responses) : this(new Queue<string>(responses)) { }
        public string Provider => "test"; public string Model => "test-model"; public int CallCount { get; private set; }
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(new ChatCompletionResult(responses.Dequeue(), Model, 10, 5)); }
    }

    private sealed class FakeFallback : IRetrievalPlanner
    {
        public ResearchRetrievalStrategy BuildPlan(string question, RetrievalMode? mode, string? documentType, int topK) => new("Auto", [new(null, "Primary", "fallback query", 5, "fallback")]);
    }
}
