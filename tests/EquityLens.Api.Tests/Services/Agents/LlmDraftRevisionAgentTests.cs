using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class LlmDraftRevisionAgentTests
{
    [Fact]
    public async Task ReviseAsync_ValidJson_ReturnsResultAndRequestsJsonObject()
    {
        var chat = new FakeChatCompletionService(
            """
            {
              "revisedAnswer": "修訂後回答。",
              "revisionSummary": "已移除未受支持的推論。",
              "appliedRecommendation": "補強引用支撐。"
            }
            """);
        var agent = new LlmDraftRevisionAgent(chat, NullLogger<LlmDraftRevisionAgent>.Instance);

        var result = await agent.ReviseAsync(CreateInput(requiresRevision: true));

        Assert.NotNull(chat.LastRequest);
        Assert.Equal(ChatResponseFormat.JsonObject, chat.LastRequest.ResponseFormat);
        Assert.Contains("json", chat.LastRequest.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("修訂後回答。", result.RevisedAnswer);
        Assert.Equal("已移除未受支持的推論。", result.RevisionSummary);
        Assert.Equal("補強引用支撐。", result.AppliedRecommendation);
    }

    [Fact]
    public async Task ReviseAsync_NoRevision_ReturnsSourceAnswerAndDoesNotCallLlm()
    {
        var chat = new FakeChatCompletionService("{}");
        var agent = new LlmDraftRevisionAgent(chat, NullLogger<LlmDraftRevisionAgent>.Instance);

        var result = await agent.ReviseAsync(CreateInput(requiresRevision: false));

        Assert.Null(chat.LastRequest);
        Assert.Equal("source answer", result.RevisedAnswer);
        Assert.Equal("CriticReview 未要求修訂，保留原回答。", result.RevisionSummary);
        Assert.Null(result.AppliedRecommendation);
    }

    [Fact]
    public async Task ReviseAsync_InvalidJson_Throws()
    {
        var agent = new LlmDraftRevisionAgent(
            new FakeChatCompletionService("not json"),
            NullLogger<LlmDraftRevisionAgent>.Instance);

        var exception = await Assert.ThrowsAsync<AgentNodeException>(() => agent.ReviseAsync(CreateInput(requiresRevision: true)));

        Assert.Equal("LLM draft revision returned invalid JSON content.", exception.Message);
    }

    [Fact]
    public async Task ReviseAsync_EmptyContent_Throws()
    {
        var agent = new LlmDraftRevisionAgent(
            new FakeChatCompletionService(""),
            NullLogger<LlmDraftRevisionAgent>.Instance);

        var exception = await Assert.ThrowsAsync<AgentNodeException>(() => agent.ReviseAsync(CreateInput(requiresRevision: true)));

        Assert.Equal("LLM draft revision returned empty JSON content.", exception.Message);
    }

    [Fact]
    public async Task ReviseAsync_MissingAppliedRecommendationWhenRequired_Throws()
    {
        var agent = new LlmDraftRevisionAgent(
            new FakeChatCompletionService(
                """
                {
                  "revisedAnswer": "修訂後回答。",
                  "revisionSummary": "已修訂。",
                  "appliedRecommendation": null
                }
                """),
            NullLogger<LlmDraftRevisionAgent>.Instance);

        var exception = await Assert.ThrowsAsync<AgentNodeException>(() => agent.ReviseAsync(CreateInput(requiresRevision: true)));

        Assert.Equal("LLM draft revision result is missing appliedRecommendation.", exception.Message);
    }

    private static DraftRevisionInput CreateInput(bool requiresRevision) => new(
        "2330",
        "question?",
        "source answer",
        requiresRevision,
        "critic summary",
        requiresRevision ? "High" : "None",
        requiresRevision
            ? [new CriticFinding("High", "InsufficientEvidence", "資料不足。", [], "補強引用。")]
            : [],
        SuggestedAnswerRevision: null,
        AppliedRecommendation: requiresRevision ? DeterministicDraftRevisionAgent.FallbackRecommendation : null);

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;

        public FakeChatCompletionService(string content)
        {
            _content = content;
        }

        public string Provider => "fake";

        public string Model => "fake-model";

        public ChatCompletionRequest? LastRequest { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatCompletionResult(_content, Model, 10, 20));
        }
    }
}
