using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class LlmCriticReviewAgentTests
{
    [Fact]
    public async Task CritiqueAsync_ValidJson_ReturnsCriticReviewResultAndRequestsJsonObject()
    {
        var chat = new FakeChatCompletionService(
            """
            {
              "summary": "發現回答有未支撐推論。",
              "overallSeverity": "Medium",
              "findings": [
                {
                  "severity": "Medium",
                  "category": "UnsupportedClaim",
                  "message": "部分結論未由引用直接支撐。",
                  "relatedCitationIndexes": [1],
                  "recommendation": "移除或補強該結論。"
                }
              ],
              "suggestedAnswerRevision": "請保留有引用支撐的內容。"
            }
            """);
        var agent = new LlmCriticReviewAgent(chat, NullLogger<LlmCriticReviewAgent>.Instance);

        var result = await agent.CritiqueAsync(CreateInput());

        Assert.NotNull(chat.LastRequest);
        Assert.Equal(ChatResponseFormat.JsonObject, chat.LastRequest.ResponseFormat);
        Assert.Contains("json", chat.LastRequest.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Medium", result.OverallSeverity);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("UnsupportedClaim", finding.Category);
        Assert.Equal(new[] { 1 }, finding.RelatedCitationIndexes);
        Assert.Equal("請保留有引用支撐的內容。", result.SuggestedAnswerRevision);
    }

    [Fact]
    public async Task CritiqueAsync_InvalidJson_Throws()
    {
        var agent = new LlmCriticReviewAgent(
            new FakeChatCompletionService("not json"),
            NullLogger<LlmCriticReviewAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.CritiqueAsync(CreateInput()));

        Assert.Equal("LLM critic returned invalid JSON content.", exception.Message);
    }

    [Fact]
    public async Task CritiqueAsync_EmptyContent_Throws()
    {
        var agent = new LlmCriticReviewAgent(
            new FakeChatCompletionService(""),
            NullLogger<LlmCriticReviewAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.CritiqueAsync(CreateInput()));

        Assert.Equal("LLM critic returned empty JSON content.", exception.Message);
    }

    [Fact]
    public async Task CritiqueAsync_InvalidSeverity_Throws()
    {
        var agent = new LlmCriticReviewAgent(
            new FakeChatCompletionService(
                """
                {
                  "summary": "bad severity",
                  "overallSeverity": "Severe",
                  "findings": [],
                  "suggestedAnswerRevision": null
                }
                """),
            NullLogger<LlmCriticReviewAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.CritiqueAsync(CreateInput()));

        Assert.Equal("LLM critic result has invalid overallSeverity 'Severe'.", exception.Message);
    }

    [Fact]
    public async Task CritiqueAsync_InvalidCitationIndex_Throws()
    {
        var agent = new LlmCriticReviewAgent(
            new FakeChatCompletionService(
                """
                {
                  "summary": "citation index too high",
                  "overallSeverity": "High",
                  "findings": [
                    {
                      "severity": "High",
                      "category": "WeakCitation",
                      "message": "引用不存在。",
                      "relatedCitationIndexes": [3],
                      "recommendation": "改用存在的引用。"
                    }
                  ],
                  "suggestedAnswerRevision": null
                }
                """),
            NullLogger<LlmCriticReviewAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.CritiqueAsync(CreateInput()));

        Assert.Equal("LLM critic finding has out-of-range citation index 3.", exception.Message);
    }

    [Fact]
    public async Task CritiqueAsync_InvalidCategory_Throws()
    {
        var agent = new LlmCriticReviewAgent(
            new FakeChatCompletionService(
                """
                {
                  "summary": "bad category",
                  "overallSeverity": "Low",
                  "findings": [
                    {
                      "severity": "Low",
                      "category": "RandomCategory",
                      "message": "分類錯誤。",
                      "relatedCitationIndexes": [],
                      "recommendation": "使用允許分類。"
                    }
                  ],
                  "suggestedAnswerRevision": null
                }
                """),
            NullLogger<LlmCriticReviewAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.CritiqueAsync(CreateInput()));

        Assert.Equal("LLM critic finding has invalid category 'RandomCategory'.", exception.Message);
    }

    private static CriticReviewInput CreateInput() => new(
        "2330",
        "question?",
        "answer [1]",
        CitationCount: 2,
        CandidateCount: 5,
        SourceStatus: "Answered",
        EvidenceFindings: []);

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
