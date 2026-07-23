using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Chat;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed class ConversationAgentTests
{
    [Fact]
    public async Task DecideAsync_UsesStrictStructuredOutputWithoutTools()
    {
        var chat = new FakeChat("""
            {"action":"DirectResponse","response":"你好！","standaloneQuery":null,"reasonCode":"Greeting","confidence":"high","contextPatch":{},"summary":"使用者打招呼"}
            """);
        var agent = new LlmConversationAgent(chat);

        var decision = await agent.DecideAsync(new("你好", new JsonObject(), [], []));

        Assert.Equal(ConversationActions.DirectResponse, decision.Action);
        Assert.Equal("你好！", decision.Response);
        Assert.Equal(ChatResponseFormat.JsonObject, chat.LastRequest?.ResponseFormat);
        Assert.DoesNotContain("searchDocuments", chat.LastRequest?.SystemPrompt);
        Assert.Contains("沒有任何工具", chat.LastRequest?.SystemPrompt);
        Assert.Contains("平行追問", chat.LastRequest?.SystemPrompt);
    }

    [Fact]
    public async Task DecideAsync_RepairsUnknownFields()
    {
        var chat = new SequenceChat(
            """{"action":"DirectResponse","response":"x","reasonCode":"Greeting","confidence":"high","contextPatch":{},"summary":"","tool":"webSearch"}""",
            """{"action":"DirectResponse","response":"你好","standaloneQuery":null,"reasonCode":"Greeting","confidence":"high","contextPatch":{},"summary":""}""");
        var agent = new LlmConversationAgent(chat);

        var decision = await agent.DecideAsync(new("你好", new JsonObject(), [], []));

        Assert.Equal("你好", decision.Response);
        Assert.Equal(2, chat.CallCount);
    }

    [Fact]
    public async Task DecideAsync_RepairsSimplifiedChinese()
    {
        var chat = new SequenceChat(
            """{"action":"DirectResponse","response":"请问有什么可以帮你？","standaloneQuery":null,"reasonCode":"Greeting","confidence":"high","contextPatch":{},"summary":""}""",
            """{"action":"DirectResponse","response":"請問有什麼可以幫你？","standaloneQuery":null,"reasonCode":"Greeting","confidence":"high","contextPatch":{},"summary":""}""");

        var decision = await new LlmConversationAgent(chat).DecideAsync(new("你好", new JsonObject(), [], []));

        Assert.Equal("請問有什麼可以幫你？", decision.Response);
        Assert.Equal(2, chat.CallCount);
    }

    [Fact]
    public async Task DecideAsync_IncludesAvailablePortfoliosInPrompt()
    {
        var chat = new FakeChat("""
            {"action":"RouteWorkflow","response":"幫你診斷投組。","standaloneQuery":"診斷投資組合「˙777」最近一年的風險","reasonCode":"portfolio_diagnosis","confidence":"high","contextPatch":{},"summary":""}
            """);
        var agent = new LlmConversationAgent(chat);
        var portfolios = new[] { new ConversationPortfolioOption(Guid.NewGuid(), "˙777") };

        var decision = await agent.DecideAsync(new("我的投資組合最近一年的風險如何？", new JsonObject(), [], portfolios));

        Assert.Equal(ConversationActions.RouteWorkflow, decision.Action);
        Assert.Contains("availablePortfolios", chat.LastRequest?.UserPrompt);
        Assert.Contains("777", chat.LastRequest?.UserPrompt);
        Assert.Contains("禁止再追問持股明細", chat.LastRequest?.SystemPrompt);
    }

    private sealed class FakeChat(string content) : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "configured";
        public ChatCompletionRequest? LastRequest { get; private set; }
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatCompletionResult(content, "conversation-test", 10, 5));
        }
    }

    private sealed class SequenceChat(params string[] contents) : IChatCompletionService
    {
        public int CallCount { get; private set; }
        public string Provider => "test";
        public string Model => "configured";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var content = contents[Math.Min(CallCount, contents.Length - 1)];
            CallCount++;
            return Task.FromResult(new ChatCompletionResult(content, "conversation-test", 10, 5));
        }
    }
}
