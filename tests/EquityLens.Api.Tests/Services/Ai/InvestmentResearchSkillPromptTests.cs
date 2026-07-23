using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai;

public sealed class InvestmentResearchSkillPromptTests
{
    [Fact]
    public async Task AnswerGenerator_ConferenceCallSkill_KeepsGlobalGuardsAndAddsSpecializedPrompt()
    {
        var chat = new CapturingChat();
        var generator = new AnswerGenerator(
            chat,
            new CitationValidator(),
            Options.Create(new RetrievalOptions()),
            NullLogger<AnswerGenerator>.Instance);

        await generator.GenerateAsync(
            "聯發科法說會相較上季改變了什麼？",
            "[1] 管理層表示需求改善。",
            null,
            0.2,
            1,
            new(
                "conference-call-takeaways",
                "conference-call-takeaways",
                1,
                InvestmentResearchSkillPrompts.ConferenceCallTakeaways));

        Assert.NotNull(chat.Request);
        Assert.Contains("Only answer using information from the provided documents.", chat.Request.SystemPrompt);
        Assert.Contains("global evidence, citation, attribution, language and non-fabrication rules above have higher priority", chat.Request.SystemPrompt);
        Assert.Contains("Guidance Delta / 指引變化", chat.Request.SystemPrompt);
        Assert.Contains("缺少基線時標記 unresolved/open item", chat.Request.SystemPrompt);
    }

    private sealed class CapturingChat : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "test-model";
        public ChatCompletionRequest? Request { get; private set; }
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ChatCompletionResult("回答 [1]", Model, 10, 5));
        }
    }
}
