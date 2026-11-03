using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using EquityLens.Api.Observability;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchAgentLoopTests
{
    private const string Search = """{"action":"call_tools","thoughtSummary":"先找證據","toolCalls":[{"callId":"c1","tool":"search_local","arguments":{"query":"營收"}}]}""";
    private const string Submit = """{"action":"submit_answer","thoughtSummary":"已有資料","answer":{"markdown":"營收 100 元 [E1]。","citedEvidenceIds":["E1"]}}""";

    [Theory]
    [InlineData("{\"action\":\"submit_answer\",\"answer\":\"private@example.invalid\"}", "$.answer", "object", "string")]
    [InlineData("{\"action\":\"submit_answer\",\"answer\":{\"markdown\":\"ok\",\"citedEvidenceIds\":\"E1\"}}", "$.answer.citedEvidenceIds", "array", "string")]
    [InlineData("{\"action\":\"submit_answer\",\"answer\":{\"markdown\":\"ok\",\"citedEvidenceIds\":[123]}}", "$.answer.citedEvidenceIds[0]", "string", "number")]
    [InlineData("{\"action\":\"call_tools\",\"toolCalls\":[{\"callId\":\"x\",\"tool\":\"search_local\",\"arguments\":[]}]} ", "$.toolCalls[0].arguments", "object", "array")]
    [InlineData("{\"action\":\"call_tools\",\"toolCalls\":[\"private@example.invalid\"]}", "$.toolCalls[0]", "object", "string")]
    [InlineData("[]", "$", "object", "array")]
    public void ProtocolErrors_ReportFieldAndTypesWithoutValues(string response, string path, string expected, string actual)
    {
        var error = Assert.ThrowsAny<JsonException>(() => LoopProtocol.Parse(response));
        Assert.Contains(path, error.Message);
        Assert.Contains("expected " + expected, error.Message);
        Assert.Contains("received " + actual, error.Message);
        Assert.DoesNotContain("private@example.invalid", error.Message);
    }

    [Fact]
    public async Task ProtocolRepair_SurvivesInterruptionWithoutRepeatingSearchOrLeakingValues()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var tools = new FakeTools();
        var malformed = """{"action":"submit_answer","answer":"private@example.invalid"}""";
        await Assert.ThrowsAsync<OperationCanceledException>(() => Loop(new FakeChat(Search, malformed), tools).RunAsync(context, "Research", default));
        var turn = await db.AgentLlmTurns.SingleAsync(x => x.Action == "parse_error");
        Assert.Contains("$.answer", turn.ErrorMessage);
        Assert.DoesNotContain("private@example.invalid", turn.ActionJson);
        Assert.Contains("string", turn.ActionJson);
        var repair = new FakeChat(Submit);
        var state = await Loop(repair, tools).RunAsync(context, "Research", default);
        Assert.Equal("AnswerSubmitted", state.StopReason);
        Assert.Equal(1, tools.Calls);
        Assert.Contains("$.answer", repair.Prompts.Single());
        Assert.DoesNotContain("private@example.invalid", repair.Prompts.Single());
        Assert.Contains(context.Run.Events, x => x.EventType == "AgentLoopProtocolRejected");
    }

    [Fact]
    public void InvalidJson_DoesNotEchoUntrustedPropertyNames()
    {
        var error = Assert.ThrowsAny<JsonException>(() => LoopProtocol.Parse("{\"private@example.invalid\": secret}"));
        Assert.DoesNotContain("private@example.invalid", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Contains("InvalidJson", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedProtocolPrompt_ProvidesValidTypedExamplesForBothRoles(bool portfolio)
    {
        await using var db = RefactorTestDbContext.Create();
        var chat = new FakeChat("""{"action":"give_up"}""");
        await new AgentLoopEngine(chat, new FakeTools(), Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, portfolio).RunAsync(await Context(db), portfolio ? "Portfolio" : "Research", default);
        var examples = chat.SystemPrompts[0].Split('\n').Where(x => x.TrimStart().StartsWith("{\"action\"")) .Select(x => LoopProtocol.Parse(x.Trim())).ToArray();
        Assert.Contains(examples, x => x.Action == "call_tools" && x.ToolCalls.Count > 0);
        Assert.Contains(examples, x => x.Action == "submit_answer" && x.Answer?.CitedEvidenceIds.Count > 0);
        Assert.Contains(examples, x => x.Action == "give_up");
    }

    [Fact]
    public async Task PreviouslyCommittedAnswer_RestoresOptionalCitationIdsWithoutAnotherLlmCall()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var turn = new AgentLlmTurn { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            Turn = 1, PromptVersion = 4, Action = "submit_answer", ActionJson = """{"action":"submit_answer","answer":{"markdown":"已提交內容"}}""" };
        db.Add(turn);
        db.Add(new AgentLoopCheckpoint { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            StateJson = AgentNodeJson.Serialize(new ResearchLoopState { PendingTurnId = turn.Id, StartedAtUtc = DateTime.UtcNow }) });
        await db.SaveChangesAsync();
        var chat = new FakeChat();
        var state = await Loop(chat, new FakeTools()).RunAsync(context, "Research", default);
        Assert.Empty(chat.Prompts);
        Assert.Equal("已提交內容", state.Answer!.Markdown);
        Assert.Empty(state.Answer.CitedEvidenceIds);
        // 新輸出必須符合明確協定，既有已提交決策才保留舊預設。
        Assert.Throws<LoopProtocolException>(() => LoopProtocol.Parse(turn.ActionJson));
    }

    [Fact]
    public async Task EvidenceWindow_PreservesHighRankedTableAndItsSourceMetadata()
    {
        await using var db = RefactorTestDbContext.Create();
        var table = Evidence() with { Result = Evidence().Result with { DocumentChunkId = Guid.NewGuid(),
            DocumentTitle = "Example Annual Report", PageNumber = 112, Content = "產品別外部收入\n" + new string('表', 1000) + "\n產品甲 120 產品乙 80", RelevanceScore = 1 } };
        var lowRanked = Enumerable.Range(0, 24).Select(_ => Evidence() with { Result = Evidence().Result with {
            DocumentChunkId = Guid.NewGuid(), Content = new string('其', 1000), RelevanceScore = 0 } });
        var tools = new FakeTools { Results = lowRanked.Append(table).ToArray() };
        var chat = new FakeChat(Search, Submit);
        await Loop(chat, tools).RunAsync(await Context(db), "Research", default);
        var shown = JsonNode.Parse(chat.Prompts[1])!["evidence"]!.AsArray();
        var item = Assert.Single(shown, x => x!["documentTitle"]!.GetValue<string>() == "Example Annual Report")!;
        Assert.Equal(table.Result.Content, item["content"]!.GetValue<string>());
        Assert.Equal(112, item["pageNumber"]!.GetValue<int>());
    }

    [Fact]
    public async Task StructuredToolResults_AreDurableAndReusedAfterInterruption()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var tools = new StructuredTools();
        var chat = new FakeChat("""{"action":"call_tools","toolCalls":[{"callId":"p1","tool":"get_portfolio_snapshot","arguments":{}}]}""");
        var loop = new AgentLoopEngine(chat, tools, Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, portfolio: true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => loop.RunAsync(context, "Portfolio", default));
        Assert.Single(await db.AgentToolCalls.Where(x => x.Status == "Succeeded").ToListAsync());
        Assert.Equal(1, tools.Calls);
        var resume = new AgentLoopEngine(new FakeChat("""{"action":"submit_answer","answer":{"markdown":"持倉已確認 [P1]","citedEvidenceIds":["P1"]}}"""), tools,
            Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, portfolio: true);
        var state = await resume.RunAsync(context, "Portfolio", default);
        Assert.Equal(1, tools.Calls);
        Assert.Single(state.Facts);
        Assert.Equal(42, AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson)["verifiedValue"]!.GetValue<int>());
    }

    [Fact]
    public async Task PortfolioBatch_AccumulatesIndependentAnalysesAndReusesThemAfterRecovery()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var tools = new AnalysisTools();
        var chat = new FakeChat("""{"action":"call_tools","toolCalls":[{"callId":"core","tool":"run_risk_analysis","arguments":{"analysis":"Core"}},{"callId":"tail","tool":"run_risk_analysis","arguments":{"analysis":"TailRisk"}}]}""");
        var loop = new AgentLoopEngine(chat, tools, Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, portfolio: true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => loop.RunAsync(context, "Portfolio", default));
        var resumed = new AgentLoopEngine(new FakeChat("""{"action":"submit_answer","answer":{"markdown":"資料已確認 [P1] [P2]","citedEvidenceIds":["P1","P2"]}}"""), tools,
            Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, portfolio: true);
        var state = await resumed.RunAsync(context, "Portfolio", default);
        Assert.Equal(2, tools.Calls);
        Assert.Equal(2, state.Facts.Count);
        var results = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson)[AgentBlackboardKeys.MathResults]!.AsArray();
        Assert.Equal(new[] { "Core", "TailRisk" }, results.Select(x => x!["operation"]!.GetValue<string>()).Order());
    }

    private sealed class AnalysisTools : IAgentLoopTools
    {
        public int Calls;
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => [new("run_risk_analysis", "Analysis", new())];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => null;
        public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var result = new JsonObject { ["operation"] = call.Arguments["analysis"]!.DeepClone(), ["value"] = .2m };
            return Task.FromResult(new LoopToolResult([], "calculated", Data: result.DeepClone().AsObject(),
                BlackboardPatch: new JsonObject { [AgentBlackboardKeys.MathResults] = new JsonArray(result) }));
        }
    }

    private sealed class StructuredTools : IAgentLoopTools
    {
        public int Calls;
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => [new("get_portfolio_snapshot", "Snapshot", new())];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => null;
        public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LoopToolResult([], "snapshot", Data: new JsonObject { ["holdingCount"] = 2 }, BlackboardPatch: new JsonObject { ["verifiedValue"] = 42 }));
        }
    }

    [Fact]
    public async Task RevisionPrompt_IncludesFeedbackAndOriginalAnswer()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board[AgentBlackboardKeys.FeedbackComment] = "請校正年度與單位。";
        board[AgentBlackboardKeys.OriginalAnswer] = "原始內容待核對";
        context.Run.BlackboardJson = board.ToJsonString();
        var chat = new FakeChat(Search, Submit);
        await Loop(chat, new FakeTools()).RunAsync(context, "Revise", default);
        var prompt = JsonNode.Parse(chat.Prompts[0])!;
        Assert.Equal("請校正年度與單位。", prompt["feedback"]!.GetValue<string>());
        Assert.Equal("原始內容待核對", prompt["originalAnswer"]!.GetValue<string>());
    }

    [Fact]
    public async Task TwoTurns_RecordDecisionsEvidenceAndToolResults()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var tool = new FakeTools();
        var state = await Loop(new FakeChat(Search, Submit), tool).RunAsync(context, "Research", default);
        Assert.Equal("AnswerSubmitted", state.StopReason);
        Assert.Equal(2, state.Turn);
        Assert.Single(state.Ledger);
        Assert.Equal(1, tool.Calls);
        Assert.Equal(2, await db.AgentLlmTurns.CountAsync());
        Assert.Equal("Succeeded", (await db.AgentToolCalls.SingleAsync()).Status);
        Assert.NotNull((await db.AgentToolCalls.SingleAsync()).AgentLlmTurnId);
        Assert.Equal(2, (await db.AgentLoopCheckpoints.SingleAsync()).Turn);
    }

    [Fact]
    public async Task Telemetry_ParentsParallelToolsToTurn_AndUsesBoundedMetricTags()
    {
        var spans = new ConcurrentBag<Activity>();
        var metrics = new ConcurrentBag<(string Name, string[] Keys)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EquityLensTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, observer) =>
        {
            if (instrument.Meter.Name == EquityLensTelemetry.MeterName) observer.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, _, tags, _) => metrics.Add((instrument.Name, tags.ToArray().Select(x => x.Key).ToArray())));
        meter.Start();
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        using var root = new Activity("test.loop").Start();
        await Loop(new FakeChat(Search, Submit), new FakeTools()).RunAsync(context, "Research", default);
        var own = spans.Where(x => x.TraceId == root.TraceId).ToList();
        var tool = Assert.Single(own, x => x.OperationName == "gen_ai.execute_tool");
        Assert.Contains(own, x => x.OperationName == "agent.loop.turn" && x.SpanId == tool.ParentSpanId);
        Assert.All(own.Where(x => x.OperationName == "gen_ai.chat"), llm =>
            Assert.Contains(own, x => x.OperationName == "agent.loop.turn" && x.SpanId == llm.ParentSpanId));
        Assert.Contains(metrics, x => x.Name == "equitylens.agent.loop.turns");
        Assert.Contains(metrics, x => x.Name == "equitylens.agent.tool.calls");
        Assert.All(metrics.Where(x => x.Name.StartsWith("equitylens.agent.loop.")), x =>
            Assert.All(x.Keys, key => Assert.Contains(key, new[] { "mode", "action", "type", "reason", "outcome" })));
    }

    [Fact]
    public async Task Revision_PreservesSuccessfulEmptyLocalSearch_ForLocalThenWeb()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board["researchRequest"] = JsonSerializer.SerializeToNode(new ResearchAskRequest("2330", "test", SourcePolicy: SourcePolicy.LocalThenWeb), AgentNodeJson.SerializerOptions);
        board["researchAgentLedger"] = new JsonArray();
        board["researchAgentLocalSearchCompleted"] = true;
        context.Run.BlackboardJson = board.ToJsonString();
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new ResearchLoopTools(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ResearchAgentLoopOptions()));
        var chat = new FakeChat("{\"action\":\"give_up\"}");
        await new ResearchAgentLoop(chat, tools, Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System).RunAsync(context, "Revise", default);
        Assert.Contains("search_web", chat.Prompts.Single());
    }

    [Fact]
    public async Task CrashAfterCommittedTurn_ResumesWithoutRepeatingTool()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var tool = new FakeTools();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Loop(new FakeChat(Search), tool).RunAsync(context, "Research", default));
        Assert.Equal(1, (await db.AgentLoopCheckpoints.SingleAsync()).Turn);
        var result = await Loop(new FakeChat(Submit), tool).RunAsync(context, "Research", default);
        Assert.Equal(1, tool.Calls);
        Assert.Equal(2, result.Turn);
        Assert.Single(await db.AgentToolCalls.ToListAsync());
    }

    [Fact]
    public async Task CrashWithinTurn_ReusesPersistedSuccessfulCall()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var turn = new AgentLlmTurn { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            Turn = 1, Action = "call_tools", ActionJson = Search, CreatedAtUtc = DateTime.UtcNow };
        db.Add(turn);
        db.Add(new AgentLoopCheckpoint { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            StateJson = AgentNodeJson.Serialize(new ResearchLoopState { PendingTurnId = turn.Id, ToolCalls = 1, StartedAtUtc = DateTime.UtcNow }) });
        db.Add(new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            AgentLlmTurnId = turn.Id, ToolCallKey = "c1", ToolName = "search_local", Status = "Succeeded",
            ResultJson = AgentNodeJson.Serialize(new LoopToolResult([Evidence()], "persisted")) });
        await db.SaveChangesAsync();
        var tool = new FakeTools();
        var result = await Loop(new FakeChat(Submit), tool).RunAsync(context, "Research", default);
        Assert.Equal(0, tool.Calls);
        Assert.Single(result.Ledger);
        Assert.Equal("AnswerSubmitted", result.StopReason);
    }

    [Fact]
    public async Task ToolCalls_RunConcurrently_AndAreAllRecorded()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var search = JsonNode.Parse(Search)!.AsObject();
        search["toolCalls"]!.AsArray().Add(new JsonObject { ["callId"] = "c2", ["tool"] = "search_local", ["arguments"] = new JsonObject { ["query"] = "毛利" } });
        var tool = new FakeTools { WaitForTwo = true };
        var result = await Loop(new FakeChat(search.ToJsonString(), Submit), tool).RunAsync(context, "Research", default);
        Assert.Equal(2, tool.MaxConcurrent);
        Assert.Equal(2, await db.AgentToolCalls.CountAsync());
        Assert.Equal(2, result.ToolCalls);
    }

    [Fact]
    public async Task GuardDenial_IsPersistedAndReturnedInNextPrompt()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var chat = new FakeChat(Search.Replace("search_local", "secret_tool"), "{\"action\":\"give_up\"}");
        var tool = new FakeTools();
        var result = await Loop(chat, tool).RunAsync(context, "Research", default);
        Assert.Equal(0, tool.Calls);
        Assert.Equal(1, result.GuardDenials);
        Assert.Equal("Denied", (await db.AgentToolCalls.SingleAsync()).Status);
        Assert.Contains("ToolNotVisible", chat.Prompts[1]);
    }

    [Fact]
    public async Task NoProgress_EmptySearches_StopWithoutAnotherModelCall_AndRemainStoppedOnResume()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var chat = new FakeChat(Search, Search);
        var tools = new FakeTools { Results = [] };
        var options = new ResearchAgentLoopOptions { MaxTurns = 10, MaxToolCalls = 10 };

        var state = await Loop(chat, tools, options).RunAsync(context, "Research", default);

        Assert.Equal("NoProgress", state.StopReason);
        Assert.Equal(2, state.NoProgressTurns);
        Assert.Equal(2, state.Turn);
        Assert.Equal(2, chat.Prompts.Count);
        Assert.Equal(2, tools.Calls);
        Assert.Empty(state.Ledger);
        Assert.Equal(ResearchAgentLoop.Refusal, state.Answer!.Markdown);
        Assert.False(state.ForcedDraft);
        Assert.Equal(2, await db.AgentToolCalls.CountAsync());
        var saved = JsonSerializer.Deserialize<ResearchLoopState>((await db.AgentLoopCheckpoints.SingleAsync()).StateJson, AgentNodeJson.SerializerOptions)!;
        Assert.Equal("NoProgress", saved.StopReason);
        Assert.Null(saved.PendingTurnId);

        var resumedChat = new FakeChat();
        var resumed = await Loop(resumedChat, tools, options).RunAsync(context, "Research", default);
        Assert.Equal("NoProgress", resumed.StopReason);
        Assert.Empty(resumedChat.Prompts);
        Assert.Equal(2, tools.Calls);
    }

    [Fact]
    public async Task NoProgress_DuplicateEvidence_AllowsOneFinalDraft_WithNoFurtherTools()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var chat = new FakeChat(Search, Search, Search, Submit);
        var tools = new FakeTools();

        var state = await Loop(chat, tools, new() { MaxTurns = 10, MaxToolCalls = 10 })
            .RunAsync(context, "Research", default);

        Assert.Equal("NoProgress", state.StopReason);
        Assert.True(state.ForcedDraft);
        Assert.Equal(2, state.NoProgressTurns);
        Assert.Single(state.Ledger);
        Assert.Equal(4, state.Turn);
        Assert.Equal(4, chat.Prompts.Count);
        Assert.Equal(3, tools.Calls);
        var finalPrompt = JsonNode.Parse(chat.Prompts[^1])!;
        Assert.Empty(finalPrompt["tools"]!.AsArray());
        Assert.Contains("不能再呼叫工具", finalPrompt["instruction"]!.GetValue<string>());
        Assert.Equal("營收 100 元 [E1]。", state.Answer!.Markdown);
        Assert.Equal(4, await db.AgentLlmTurns.CountAsync());
        Assert.Equal(3, await db.AgentToolCalls.CountAsync());
    }

    [Fact]
    public async Task InvalidProtocolTwice_StopsWithoutThrowing()
    {
        await using var db = RefactorTestDbContext.Create();
        var state = await Loop(new FakeChat("not-json", "{}"), new FakeTools()).RunAsync(await Context(db), "Research", default);
        Assert.Equal("ProtocolFailure", state.StopReason);
        Assert.Equal(ResearchAgentLoop.Refusal, state.Answer!.Markdown);
        Assert.Equal(2, await db.AgentLlmTurns.CountAsync(x => x.Action == "parse_error"));
    }

    [Fact]
    public async Task LargeEvidenceLedger_IsWindowedBeforeBudgetCheck_InsteadOfPrematureRefusal()
    {
        await using var db = RefactorTestDbContext.Create();
        var chat = new FakeChat(Search, Submit);
        var tools = new FakeTools { Results = Enumerable.Range(0, 30).Select(_ => Evidence() with
            { Result = Evidence().Result with { DocumentChunkId = Guid.NewGuid(), Content = "營收 100 元。" + new string('證', 3000) } }).ToList() };
        var result = await Loop(chat, tools, new() { MaxPromptTokens = 9000 }).RunAsync(await Context(db), "Research", default);
        Assert.Equal("AnswerSubmitted", result.StopReason);
        Assert.Equal(30, result.Ledger.Count);
        var prompt = JsonNode.Parse(chat.Prompts[1])!;
        Assert.InRange(prompt["evidence"]!.AsArray().Count, 1, 29);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(chat.Prompts[1]) < 9000);
        Assert.Contains("營收", chat.Prompts[1]); // 避免中文變成六字元的 Unicode escape。
    }

    [Fact]
    public async Task TokenBudget_IsCheckedBeforeCallingProvider()
    {
        await using var db = RefactorTestDbContext.Create();
        var chat = new FakeChat();
        var state = await Loop(chat, new FakeTools(), new() { MaxPromptTokens = 1 }).RunAsync(await Context(db), "Research", default);
        Assert.Equal("BudgetExhausted", state.StopReason);
        Assert.Empty(chat.Prompts);
    }

    [Fact]
    public async Task SameRoundBudget_IsReservedBeforeDispatch()
    {
        await using var db = RefactorTestDbContext.Create();
        var action = JsonNode.Parse(Search)!;
        action["toolCalls"]!.AsArray().Add(new JsonObject { ["callId"] = "c2", ["tool"] = "search_local", ["arguments"] = new JsonObject { ["query"] = "second" } });
        var tool = new FakeTools();
        var state = await Loop(new FakeChat(action.ToJsonString(), Submit), tool, new() { MaxToolCalls = 1 })
            .RunAsync(await Context(db), "Research", default);
        Assert.Equal(1, tool.Calls);
        Assert.Equal(1, state.GuardDenials);
        Assert.Equal("BudgetExhausted", state.StopReason);
        Assert.Contains("[E1]", state.Answer!.Markdown);
    }

    [Theory]
    [InlineData(SourcePolicy.LocalOnly, false, false)]
    [InlineData(SourcePolicy.LocalThenWeb, false, false)]
    [InlineData(SourcePolicy.LocalThenWeb, true, true)]
    [InlineData(SourcePolicy.WebOnly, false, true)]
    public void SourcePolicy_IsEnforcedByVisibilityAndValidation(SourcePolicy policy, bool searched, bool webAllowed)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new ResearchLoopTools(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ResearchAgentLoopOptions()));
        var scope = new LoopToolScope(Guid.NewGuid(), new("2330", "test", SourcePolicy: policy), [], searched);
        var call = new LoopToolCall("c", "search_web", new JsonObject { ["query"] = "營收" });
        Assert.Equal(webAllowed, tools.Visible(scope).Any(x => x.Name == "search_web"));
        Assert.Equal(webAllowed, tools.Validate(scope, call) is null);
        if (policy == SourcePolicy.WebOnly) Assert.DoesNotContain(tools.Visible(scope), x => x.Name == "search_local");
    }

    [Theory]
    [InlineData("AnnualReport", true)]
    [InlineData("EarningsPresentation", true)]
    [InlineData("財報", false)]
    public void DocumentType_IsEnumeratedAndGuardedBeforeDispatch(string type, bool allowed)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new ResearchLoopTools(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ResearchAgentLoopOptions()));
        var scope = new LoopToolScope(Guid.NewGuid(), new("2330", "test", SourcePolicy: SourcePolicy.LocalOnly), [], false);
        var definition = tools.Visible(scope).Single(x => x.Name == "search_local");
        Assert.Equal(new[] { "AnnualReport", "EarningsPresentation" }, definition.Parameters["properties"]!["documentType"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(allowed, tools.Validate(scope, new("c", "search_local", new JsonObject { ["query"] = "EPS", ["documentType"] = type })) is null);
    }

    [Fact]
    public async Task ResearchPrompt_ExplainsSourceRolesAndIncludesEvidenceMetadata()
    {
        await using var db = RefactorTestDbContext.Create();
        var chat = new FakeChat(Search, Submit);
        await Loop(chat, new FakeTools()).RunAsync(await Context(db), "Research", default);
        Assert.Contains("Primary", chat.SystemPrompts[0]);
        Assert.Contains("Supporting", chat.SystemPrompts[0]);
        Assert.Contains("風險題不可把年報揭露說成法說會提到", chat.SystemPrompts[0]);
        Assert.Contains("目錄頁與封面", chat.SystemPrompts[0]);
        var item = JsonNode.Parse(chat.Prompts[1])!["evidence"]![0]!;
        Assert.Equal("AnnualReport", item["documentType"]!.GetValue<string>());
        Assert.Equal("Primary", item["sourceRole"]!.GetValue<string>());
    }

    [Fact]
    public async Task PortfolioOptimization_GuardRequiresScenarioBeforeAcceptingAnswer()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board[AgentBlackboardKeys.RoutingContext] = new JsonObject { ["objective"] = "優化投組" };
        board[AgentBlackboardKeys.MathInputs] = new JsonObject { ["commonTradingDays"] = 180 };
        board[PortfolioLoopTools.SnapshotKey] = new JsonObject();
        context.Run.BlackboardJson = board.ToJsonString();
        var answer = """{"action":"submit_answer","answer":{"markdown":"已比較情境[P1]","citedEvidenceIds":["P1"]}}""";
        var chat = new FakeChat(answer, """{"action":"call_tools","toolCalls":[{"callId":"scenario","tool":"simulate_weight_scenario","arguments":{}}]}""", answer);
        var state = await new AgentLoopEngine(chat, new ScenarioTools(), Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System, true).RunAsync(context, "Portfolio", default);
        Assert.Equal("AnswerSubmitted", state.StopReason);
        Assert.Equal(1, state.GuardDenials);
        Assert.Contains("OptimizationScenarioRequired", chat.Prompts[1]);
        Assert.Single(await db.AgentToolCalls.ToListAsync());
        Assert.Contains(context.Run.Events, e => e.EventType == "AnswerGuardDenied");
    }
    private sealed class ScenarioTools : IAgentLoopTools
    {
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => [new("simulate_weight_scenario", "fixture", new())];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => null;
        public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct) => Task.FromResult(new LoopToolResult([], "calculated",
            Data: new() { ["status"] = "Calculated" }, BlackboardPatch: new() { [PortfolioScenarioCalculator.ResultKey] = new JsonObject { ["status"] = "Calculated" } }));
    }

    [Fact]
    public void PublishedMethods_PreserveConferencePromptAndSeparateRoles()
    {
        var catalog = new AgentMethodCatalog(Path.Combine(AppContext.BaseDirectory, "AgentSkills"));
        Assert.Equal(2, catalog.ForRole("Research").Count);
        Assert.Single(catalog.ForRole("Portfolio"));
        var conference = catalog.ForRole("Research").Single(x => x.Id == "conference-call-takeaways");
        // 從移動前的原始法說會 prompt 核對，不重新生成內容。
        Assert.Equal("47a678014da54fa1c8bac8f65bf3358d62936533c629f8cdd65ac41db5237b03", conference.ContentHash);
    }

    [Fact]
    public async Task Skills_AreRoleScopedBoundedAuditedAndCannotExpandTools()
    {
        var directory = Path.Combine(Path.GetTempPath(), "method-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var (id, role, content) in new[] { ("one", "Research", "METHOD_ONE"), ("two", "Research", "METHOD_TWO"), ("private", "Portfolio", "PRIVATE_METHOD") })
                File.WriteAllText(Path.Combine(directory, id + ".json"), AgentNodeJson.Serialize(new AgentMethod(id, id, "description", role, "1", content)));
            var catalog = new AgentMethodCatalog(directory);
            await using var db = RefactorTestDbContext.Create();
            var context = await Context(db);
            var chat = new FakeChat("""{"action":"call_tools","toolCalls":[{"callId":"1","tool":"load_skill","arguments":{"id":"one"}},{"callId":"2","tool":"load_skill","arguments":{"id":"two"}},{"callId":"3","tool":"load_skill","arguments":{"id":"private"}},{"callId":"4","tool":"search_web","arguments":{"query":"x"}}]}""", """{"action":"give_up"}""");
            var state = await new AgentLoopEngine(chat, new FakeTools(), Options.Create(new ResearchAgentLoopOptions { MaxSkills = 1 }), TimeProvider.System, skills: catalog).RunAsync(context, "Research", default);
            Assert.DoesNotContain("METHOD_ONE", chat.SystemPrompts[0]);
            Assert.Contains("METHOD_ONE", chat.SystemPrompts[1]);
            Assert.DoesNotContain("PRIVATE_METHOD", string.Join("", chat.SystemPrompts));
            Assert.DoesNotContain("private", chat.SystemPrompts[0]);
            var calls = await db.AgentToolCalls.ToListAsync();
            Assert.Equal(4, calls.Count);
            Assert.Contains(calls, c => c.GuardReason == "SkillLimitExceeded");
            Assert.Contains(calls, c => c.GuardReason == "SkillNotVisible");
            Assert.Contains(calls, c => c.ToolName == "search_web" && c.Status == "Denied");
            var loaded = JsonNode.Parse(calls.Single(c => c.Status == "Succeeded").ResultJson!)!["data"]!;
            Assert.Equal("one", loaded["skillId"]!.GetValue<string>());
            Assert.Equal("1", loaded["version"]!.GetValue<string>());
            Assert.Equal(catalog.ForRole("Research")[0].ContentHash, loaded["contentHash"]!.GetValue<string>());
            Assert.Empty(state.Facts); // 方法不是可引用的事實。
            File.WriteAllText(Path.Combine(directory, "third.json"), AgentNodeJson.Serialize(new AgentMethod("third", "third", "added", "Research", "1", "NEW_METHOD")));
            Assert.Equal(3, catalog.ForRole("Research").Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ResearchAgentLoop Loop(FakeChat chat, FakeTools tools, ResearchAgentLoopOptions? options = null) =>
        new(chat, tools, Options.Create(options ?? new ResearchAgentLoopOptions()), TimeProvider.System);

    private static async Task<AgentNodeExecutionContext> Context(RefactorTestDbContext db)
    {
        var run = new AgentRun { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "Running",
            BlackboardJson = AgentNodeJson.Serialize(new { researchRequest = new ResearchAskRequest("2330", "營收多少", SourcePolicy: SourcePolicy.LocalOnly) }) };
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "loop", NodeType = "ResearchAgentLoop", Status = "Running" };
        run.Nodes.Add(node); db.Add(run); await db.SaveChangesAsync();
        return new(db, run, node, (r, n, type, message, payload) => r.Events.Add(new AgentRunEvent { Id = Guid.NewGuid(), AgentRunId = r.Id,
            AgentRunNodeId = n?.Id, EventType = type, Message = message, PayloadJson = AgentNodeJson.Serialize(payload) }));
    }

    private static RetrievedDocumentChunk Evidence() => new(new DocumentSearchResult(
        Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"),
        "年報", "AnnualReport", null, 0, 1, null, "營收 100 元", 0, 1, null, "2330", null, null), "Primary", "test", "營收");

    private sealed class FakeChat(params string[] responses) : IChatCompletionService
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> Prompts { get; } = [];
        public List<string> SystemPrompts { get; } = [];
        public string Provider => "test";
        public string Model => "fixture";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Prompts.Add(request.UserPrompt);
            SystemPrompts.Add(request.SystemPrompt);
            if (_responses.Count == 0) throw new OperationCanceledException("simulated process interruption");
            return Task.FromResult(new ChatCompletionResult(_responses.Dequeue(), Model, 10, 10));
        }
    }

    [Fact]
    public async Task SearchTool_PreservesFallbackEvidenceAndRerankDiagnostics()
    {
        var services = new ServiceCollection().AddSingleton<IRetrievalPlanner>(new RetrievalPlanner(Options.Create(new RetrievalOptions())))
            .AddSingleton<IIntentDetector, IntentDetector>().AddSingleton<IDocumentRetriever, SearchFixture>().AddSingleton<IResultReranker, FallbackRanking>();
        using var provider = services.BuildServiceProvider();
        var tools = new ResearchLoopTools(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ResearchAgentLoopOptions()));
        var result = await tools.InvokeAsync(new(Guid.NewGuid(), new("2330", "營收"), [], false), new("search", "search_local", new() { ["query"] = "營收" }), default);
        Assert.Single(result.Evidence); Assert.True(result.Diagnostics!["usedFallback"]!.GetValue<bool>());
        Assert.Equal("TimedOut", result.Diagnostics["status"]!.GetValue<string>());
        Assert.Equal("RerankTimeout", result.Diagnostics["fallbackReason"]!.GetValue<string>());
        var persisted = JsonSerializer.Deserialize<LoopToolResult>(AgentNodeJson.Serialize(result), AgentNodeJson.SerializerOptions)!;
        Assert.Equal(result.Diagnostics.ToJsonString(), persisted.Diagnostics!.ToJsonString());
    }
    private sealed class SearchFixture : IDocumentRetriever
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveAsync(ResearchRetrievalStrategy strategy, string ticker, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>([Evidence()]);
        public IReadOnlyList<RetrievedDocumentChunk> GetCandidatesForRerank(IReadOnlyList<RetrievedDocumentChunk> chunks, int targetCount) => chunks;
    }
    private sealed class FallbackRanking : IResultReranker
    {
        public Task<RankedSelection> Rank(IReadOnlyList<RetrievedDocumentChunk> chunks, ResearchQuestionIntent intent, int topK, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RankedSelection(chunks, [], new("reranker", "TimedOut", null, true, "RerankTimeout", 10000, chunks.Count, chunks.Count, 100, null)));
    }

    private sealed class FakeTools : IResearchLoopTools
    {
        public IReadOnlyList<RetrievedDocumentChunk>? Results;
        public int Calls;
        public int MaxConcurrent;
        private int _active;
        public bool WaitForTwo;
        private readonly TaskCompletionSource _two = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => [new("search_local", "test", new())];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => call.Tool == "search_local" ? null : "ToolNotVisible";
        public async Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var active = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(active, MaxConcurrent);
            if (active == 2) _two.TrySetResult();
            if (WaitForTwo) await _two.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
            Interlocked.Decrement(ref _active);
            return new(Results ?? [Evidence()], "Found evidence");
        }
    }
}
