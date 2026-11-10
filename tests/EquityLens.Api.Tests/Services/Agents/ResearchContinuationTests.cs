using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed partial class ResearchAgentLoopTests
{
    [Theory]
    [InlineData("[E1]", true)]
    [InlineData("【E1】", true)]
    [InlineData("〔E1〕", true)]
    [InlineData("［E1］", true)]
    [InlineData("【E1，E99】", false)]
    public async Task ResearchCitations_NormalizeTypographyButRejectUnknownEvidence(string citation, bool accepted)
    {
        await using var db = RefactorTestDbContext.Create(); var context = await Context(db);
        context.Run.ResearchRunId = Guid.NewGuid();
        db.Add(new ResearchRun { Id = context.Run.ResearchRunId.Value, UserId = context.Run.UserId, Ticker = "2330", Question = "營收多少", Answer = "", TraceId = "t", RetrievalMode = "Auto", SourcePolicy = "LocalOnly" });
        await db.SaveChangesAsync();
        var state = await Loop(new FakeChat(Search, Submit.Replace("[E1]", citation)), new FakeTools()).RunAsync(context, "Research", default);
        await ResearchAgentOutput.WriteAsync(context, state, false, default);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        Assert.Contains("[1]", board[AgentBlackboardKeys.Answer]!.GetValue<string>());
        Assert.Equal(accepted, ResearchOutputCheck.Check(board).Passed);
        if (!accepted) Assert.Contains("DanglingEvidenceReference", ResearchOutputCheck.Check(board).BlockingIssues);
    }

    [Fact]
    public async Task NativeResearchPrompt_RetainsEvidenceCitationFormat()
    {
        await using var db = RefactorTestDbContext.Create(); var chat = new NativeChat(Submit);
        await new AgentLoopEngine(chat, new FakeTools(), Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System).RunAsync(await Context(db), "Research", default);
        Assert.Contains("[E1]", chat.Requests[0].SystemPrompt);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ResearchTransfer_OnlyImportsRevalidatedAcceptedFacts(bool passed, bool verified)
    {
        await using var db = RefactorTestDbContext.Create(); var context = await Context(db);
        context.Run.ResearchRunId = Guid.NewGuid(); context.Run.WorkflowType = AgentWorkflowTypes.ResearchAgent;
        db.Add(new ResearchRun { Id = context.Run.ResearchRunId.Value, UserId = context.Run.UserId, Ticker = "2330", Question = "營收多少", Answer = "", TraceId = "t", RetrievalMode = "Auto", SourcePolicy = "LocalOnly" }); await db.SaveChangesAsync();
        var state = await Loop(new FakeChat(Search, Submit), new FakeTools()).RunAsync(context, "Research", default);
        await ResearchAgentOutput.WriteAsync(context, state, false, default);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board["outputGate"] = new JsonObject { ["passed"] = passed };
        context.Run.BlackboardJson = board.ToJsonString(); context.Run.Status = AgentRunStatuses.Succeeded;
        await db.SaveChangesAsync();
        var source = await PortfolioResearchContext.LoadAsync(db, context.Run.UserId, context.Run.Id, default);
        Assert.Equal(verified ? "Verified" : "Unavailable", source!["status"]!.GetValue<string>());
        Assert.Equal(verified, source.ContainsKey("verifiedNumbers"));
        if (verified) Assert.Equal(100m, source["verifiedNumbers"]![0]!.GetValue<decimal>());
        await Assert.ThrowsAsync<AgentNodeException>(() => PortfolioResearchContext.LoadAsync(db, Guid.NewGuid(), context.Run.Id, default));
        // A stored pass flag cannot upgrade a subsequently altered, unsupported answer.
        board[AgentBlackboardKeys.Answer] = "營收 999999 元 [1]。";
        (await db.ResearchRuns.SingleAsync()).Answer = "營收 999999 元 [1]。";
        context.Run.BlackboardJson = board.ToJsonString(); await db.SaveChangesAsync();
        Assert.Equal("Unavailable", (await PortfolioResearchContext.LoadAsync(db, context.Run.UserId, context.Run.Id, default))!["status"]!.GetValue<string>());
    }
}
