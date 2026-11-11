using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchPrecisionTests
{
    [Theory]
    [InlineData("599 元 ÷ 1000 元 ≈ 59.9% [1]。", false)]
    [InlineData("599 元 ÷ 1000 元 ≈ 60.0% [1]。", true)]
    [InlineData("599 元 ÷ 1000 元 ≈ 60% [1]。", false)]
    [InlineData("123 元 ÷ 1000 元 ≈ 12.3% [1]。", false)]
    [InlineData("123 元 ÷ 1000 元 ≈ 12.30% [1]。", false)]
    [InlineData("123 元 ÷ 1000 元 ≈ 12.31% [1]。", true)]
    [InlineData("由 1000 元增至 1123 元，年增率 12.31% [1]。", true)]
    [InlineData("59.9% − 12.3% ≈ 47.61百分點 [1]。", true)]
    [InlineData("59.9% − 12.3% ≈ 47.6百分點 [1]。", false)]
    [InlineData("(1123 元 − 1000 元) ÷ 1000 元 ≈ 12.3% [1]。", false)]
    [InlineData("(1123 元 - 1000 元) / 1000 元 ≈ 12.31% [1]。", true)]
    [InlineData("(1123 元 - 1000 元) / 599 元 ≈ 20.5% [1]。", true)]
    [InlineData("(599 元 - 1000 元) / 1000 元 ≈ -40.1% [1]。", false)]
    [InlineData("(599 元 − 1000 元) ÷ 1000 元 ≈ −40.1% [1]。", false)]
    [InlineData("(599 元 − 1000 元) ÷ 1000 元 ≈ −40.11% [1]。", true)]
    public void DerivedValuesRespectDisplayedPrecision(string answer, bool blocking)
    {
        JsonObject[] citations = [new() { ["citationIndex"] = 1, ["quoteText"] = "599 元、1000 元、123 元、1123 元，59.9%、12.3%" }];
        Assert.Equal(blocking, ResearchNumberTraceability.Check(answer, citations).Blocking);
    }

    [Theory]
    [InlineData("doc", 8, "（承前頁）", true, false)]
    [InlineData("other", 8, "（承前頁）", true, true)]
    [InlineData("doc", 9, "（承前頁）", true, true)]
    [InlineData("doc", 8, "新表", true, true)]
    [InlineData("doc", 8, "（承前頁）", false, true)]
    public void UnitInheritanceRequiresCitedAdjacentContinuation(string doc, int page, string heading, bool citeHeader, bool blocking)
    {
        JsonObject[] citations = [new() { ["citationIndex"] = 1, ["documentId"] = "doc", ["pageNumber"] = 7,
            ["quoteText"] = "單位：新台幣百萬元。表格。（接次頁）" },
            new() { ["citationIndex"] = 2, ["documentId"] = doc, ["pageNumber"] = page, ["quoteText"] = heading + "\n本年淨利 127" }];
        Assert.Equal(blocking, ResearchNumberTraceability.Check("本年淨利 127 百萬元 [2]" + (citeHeader ? "[1]" : "") + "。", citations).Blocking);
    }
}

public sealed partial class ResearchAgentLoopTests
{
    [Fact]
    public async Task ResearchRepair_CorrectsNumericErrorBeforeFinalGate()
    {
        await using var db = RefactorTestDbContext.Create();
        var bad = Submit.Replace("100 元", "999 元");
        var chat = new FakeChat(Search, bad, Submit);
        var state = await Loop(chat, new FakeTools()).RunAsync(await Context(db), "Research", default);
        Assert.Equal(1, state.ResearchRepairCount);
        Assert.Equal("AnswerSubmitted", state.StopReason);
        Assert.Equal("營收 100 元 [E1]。", state.Answer!.Markdown);
        Assert.Contains("ResearchAnswerRepair", chat.Prompts[2]);
    }

    [Fact]
    public async Task ResearchRepair_IsBoundedAndSurvivesRecovery()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        const string partial = """{"action":"submit_answer","answer":{"markdown":"營收 100 元 [E1]；其餘數據無法核實。","citedEvidenceIds":["E1"]}}""";
        var tools = new FakeTools();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Loop(new FakeChat(Search, partial), tools).RunAsync(context, "Research", default));
        var state = await Loop(new FakeChat(partial, partial), tools).RunAsync(context, "Research", default);
        Assert.Equal(2, state.ResearchRepairCount);
        Assert.Equal(1, tools.Calls);
        Assert.Contains("無法核實", state.Answer!.Markdown);
        Assert.Equal("AnswerSubmitted", state.StopReason);
    }

    [Fact]
    public async Task RecentEvidenceIsRetainedUnderShrinkingBudgetAndWindowIsAudited()
    {
        await using var db = RefactorTestDbContext.Create();
        var context = await Context(db);
        var state = new ResearchLoopState { StartedAtUtc = DateTime.UtcNow,
            Ledger = Enumerable.Range(1, 30).Select(i => new LoopEvidence($"E{i}", Evidence() with {
                Result = Evidence().Result with { DocumentChunkId = Guid.NewGuid(), Content = "資料" + new string('字', 2000), RelevanceScore = i == 30 ? 0 : 1 } })).ToList(),
            RecentEvidenceIds = ["E30"] };
        db.Add(new AgentLoopCheckpoint { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id, StateJson = AgentNodeJson.Serialize(state) });
        await db.SaveChangesAsync();
        var chat = new FakeChat("""{"action":"give_up"}""");
        await Loop(chat, new FakeTools(), new() { MaxPromptTokens = 14000 }).RunAsync(context, "Research", default);
        var prompt = JsonNode.Parse(Assert.Single(chat.Prompts))!;
        Assert.Equal("E30", prompt["evidence"]![0]!["id"]!.GetValue<string>());
        Assert.Contains(context.Run.Events, e => e.EventType == "ResearchEvidenceWindow");
    }
    [Fact]
    public async Task ExplicitPageReadIncludesOnlyLinkedAdjacentPagesOfSameDocument()
    {
        await using var db = RefactorTestDbContext.Create();
        var source = Evidence();
        var document = source.Result.DocumentId;
        db.DocumentChunks.AddRange(
            new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document, ChunkIndex = 6, PageNumber = 7, Content = "單位：新台幣百萬元（接次頁）" },
            new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document, ChunkIndex = 7, PageNumber = 8, Content = "（承前頁）本年淨利 127" },
            new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document, ChunkIndex = 8, PageNumber = 9, Content = "無關下一表" },
            new DocumentChunk { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 6, PageNumber = 7, Content = "別家公司的單位" });
        await db.SaveChangesAsync();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<EquityLens.Api.Data.EquityLensDbContext>(services, db);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider);
        var tools = new ResearchLoopTools(scopes, Options.Create(new ResearchAgentLoopOptions()));
        var result = await tools.InvokeAsync(new(Guid.NewGuid(), new("2330", "財報"), [new("E1", source)], true),
            new("page", "get_document_page", new() { ["documentId"] = document.ToString(), ["pageNumber"] = 8 }), default);
        Assert.Equal(new int?[] { 7, 8 }, result.Evidence.Select(e => e.Result.PageNumber).Order().ToArray());
        Assert.All(result.Evidence, e => Assert.Equal(document, e.Result.DocumentId));
        Assert.Contains(result.Evidence, e => e.Result.Content == "（承前頁）本年淨利 127");
    }

}
