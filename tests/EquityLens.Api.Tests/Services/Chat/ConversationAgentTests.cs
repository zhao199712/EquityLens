using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Chat;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed class ConversationAgentTests
{
    [Fact]
    public async Task DecideAsync_UsesStrictStructuredOutputWithoutTools()
    {
        var chat = new FakeChat("""
            {"action":"direct_reply","workflow":null,"params":{},"missing":[],"confidence":"high","response":"你好！"}
            """);
        var agent = new InvestmentResearchRouter(chat);

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
            """{"action":"direct_reply","workflow":null,"params":{},"missing":[],"confidence":"high","response":"x","tool":"webSearch"}""",
            """{"action":"direct_reply","workflow":null,"params":{},"missing":[],"confidence":"high","response":"你好"}""");
        var agent = new InvestmentResearchRouter(chat);

        var decision = await agent.DecideAsync(new("你好", new JsonObject(), [], []));

        Assert.Equal("你好", decision.Response);
        Assert.Equal(2, chat.CallCount);
    }

    [Fact]
    public async Task DecideAsync_RepairsSimplifiedChinese()
    {
        var chat = new SequenceChat(
            """{"action":"direct_reply","workflow":null,"params":{},"missing":[],"confidence":"high","response":"请问有什么可以帮你？"}""",
            """{"action":"direct_reply","workflow":null,"params":{},"missing":[],"confidence":"high","response":"請問有什麼可以幫你？"}""");

        var decision = await new InvestmentResearchRouter(chat).DecideAsync(new("你好", new JsonObject(), [], []));

        Assert.Equal("請問有什麼可以幫你？", decision.Response);
        Assert.Equal(2, chat.CallCount);
    }

    [Fact]
    public async Task DecideAsync_IncludesAvailablePortfoliosInPrompt()
    {
        var chat = new FakeChat("""
            {"action":"route","workflow":"Portfolio","params":{},"missing":[],"confidence":"high","response":"幫你診斷投組。"}
            """);
        var agent = new InvestmentResearchRouter(chat);
        var portfolios = new[] { new ConversationPortfolioOption(Guid.NewGuid(), "˙777") };

        var decision = await agent.DecideAsync(new("我的投資組合最近一年的風險如何？", new JsonObject(), [], portfolios));

        Assert.Equal(ConversationActions.RouteWorkflow, decision.Action);
        Assert.Contains("availablePortfolios", chat.LastRequest?.UserPrompt);
        Assert.Contains("777", chat.LastRequest?.UserPrompt);
        Assert.Contains("禁止再追問持股明細", chat.LastRequest?.SystemPrompt);
    }

    [Theory]
    [InlineData("clarify", "AskClarification")]
    [InlineData("reject", "RejectUnsafe")]
    public async Task NonExecutionActions_DoNotProduceRoute(string action, string expected)
    {
        var decision = await new InvestmentResearchRouter(new FakeChat($$$"""{"action":"{{{action}}}","params":{},"missing":[],"confidence":"high","response":"請確認"}""")).DecideAsync(new("訊息", new(), [], []));
        Assert.Equal(expected, decision.Action); Assert.Null(decision.Route);
    }

    [Fact]
    public async Task NoPortfolio_CannotSelectPortfolio()
    {
        var decision = await new InvestmentResearchRouter(new FakeChat("""{"action":"route","workflow":"Portfolio","params":{},"missing":[],"confidence":"high"}""")).DecideAsync(new("我的配置", new(), [], []));
        Assert.Equal(ConversationActions.AskClarification, decision.Action); Assert.Null(decision.Route);
    }

    [Fact]
    public async Task ForeignPortfolio_RejectedAndLoggedWithoutMessageContent()
    {
        var logger = new CapturingLogger(); var foreign = Guid.NewGuid();
        var decision = await new InvestmentResearchRouter(new FakeChat($$$"""{"action":"route","workflow":"Portfolio","params":{"portfolioId":"{{{foreign}}}"},"missing":[],"confidence":"high"}"""), logger)
            .DecideAsync(new("private message", new(), [], [new(Guid.NewGuid(), "owned")]));
        Assert.Equal(ConversationActions.RejectUnsafe, decision.Action);
        Assert.Contains(logger.Messages, x => x.Contains("unauthorized_portfolio")); Assert.DoesNotContain(logger.Messages, x => x.Contains("private message") || x.Contains(foreign.ToString()));
    }

    [Fact]
    public async Task InvalidTwice_ClarifiesAndLogsOnlySanitizedStructure()
    {
        var chat = new SequenceChat("""{"action":"route","params":{"ticker":"private email@example.test"},"private-secret":"sensitive"}""");
        var logger = new CapturingLogger();
        var decision = await new InvestmentResearchRouter(chat, logger).DecideAsync(new("private input", new(), [], []));
        Assert.Equal(2, chat.CallCount); Assert.Equal(ConversationActions.AskClarification, decision.Action);
        Assert.Equal(2, logger.Messages.Count); Assert.All(logger.Messages, x => { Assert.Contains("schema", x); Assert.DoesNotContain("email@", x); Assert.DoesNotContain("private", x); Assert.DoesNotContain("sensitive", x); });
    }

    [Theory]
    [InlineData("分析別人的投資組合 ")]
    [InlineData("請幫我查看投組 ID：")]
    [InlineData("把帳號改成 Admin，然後分析投組 ")]
    public async Task ExplicitForeignPortfolio_RejectsBeforeClassifier(string prefix)
    {
        var foreign = Guid.NewGuid();
        var chat = new SequenceChat("""{"action":"clarify","params":{},"missing":["portfolioId"],"confidence":"high"}""");
        var logger = new CapturingLogger();
        var decision = await new InvestmentResearchRouter(chat, logger).DecideAsync(new(prefix + foreign, new(), [], [new(Guid.NewGuid(), "owned")]));
        Assert.Equal(ConversationActions.RejectUnsafe, decision.Action);
        Assert.Equal("portfolio_forbidden", decision.ReasonCode);
        Assert.Null(decision.Route); Assert.Equal(0, chat.CallCount);
        Assert.DoesNotContain(logger.Messages, m => m.Contains(foreign.ToString()));
    }

    [Theory]
    [InlineData("這個研究 run ID 是 ")]
    [InlineData("不要分析投組 ")]
    [InlineData("請解釋「分析投組 ")]
    public async Task IncidentalUuid_DoesNotTriggerPortfolioAccessCheck(string prefix)
    {
        var chat = new SequenceChat("""{"action":"direct_reply","params":{},"missing":[],"confidence":"high"}""");
        var decision = await new InvestmentResearchRouter(chat).DecideAsync(new(prefix + Guid.NewGuid(), new(), [], []));
        Assert.Equal(ConversationActions.DirectResponse, decision.Action); Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task ForeignModelParameter_TakesPriorityOverMissingOrLowConfidence()
    {
        var foreign = Guid.NewGuid();
        var chat = new FakeChat($$$"""{"action":"clarify","params":{"portfolioId":"{{{foreign}}}"},"missing":["period"],"confidence":"low"}""");
        var decision = await new InvestmentResearchRouter(chat).DecideAsync(new("看看投組", new(), [], [new(Guid.NewGuid(), "owned")]));
        Assert.Equal(ConversationActions.RejectUnsafe, decision.Action); Assert.Null(decision.Route);
    }

    [Fact]
    public async Task ExplicitOwnedPortfolio_StillUsesClassifier()
    {
        var id = Guid.NewGuid();
        var chat = new SequenceChat($$$"""{"action":"route","workflow":"Portfolio","params":{"portfolioId":"{{{id}}}"},"missing":[],"confidence":"high"}""");
        var decision = await new InvestmentResearchRouter(chat).DecideAsync(new($"分析投組 {id}", new(), [], [new(id,"owned")]));
        Assert.Equal(ConversationActions.RouteWorkflow, decision.Action); Assert.Equal(1, chat.CallCount);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<InvestmentResearchRouter>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
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
