using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class EvidenceReanalysisWorkflowTests
{
    [Fact]
    public void Definition_IsStatefulSevenNodeDag()
    {
        var run = new EvidenceReanalysisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var definition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
        Assert.Equal(AgentWorkflowTypes.EvidenceReanalysis, run.WorkflowType); Assert.Equal(AgentTypes.Analysis, run.AgentType); Assert.Equal(7, run.Nodes.Count); Assert.Equal("Stateful", definition["orchestrationMode"]!.GetValue<string>()); Assert.Equal(1, definition["version"]!.GetValue<int>()); Assert.Equal(6, definition["edges"]!.AsArray().Count);
        Assert.Equal([EvidenceReanalysisNodeTypes.Load, EvidenceReanalysisNodeTypes.Validate, EvidenceReanalysisNodeTypes.BuildContext, EvidenceReanalysisNodeTypes.Reanalyze, EvidenceReanalysisNodeTypes.Critique, EvidenceReanalysisNodeTypes.Revise, EvidenceReanalysisNodeTypes.Finalize], run.Nodes.Select(x => x.NodeType));
    }

    [Fact]
    public async Task Load_ValidSource_UsesOutputAndCopiesContext()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = Source(userId); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceReanalysisWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.ElementAt(0);
        await new LoadEvidenceRemediationNodeHandler().ExecuteAsync(new(db, run, node, (_, _, _, _, _) => { }));
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); Assert.True(board[AgentBlackboardKeys.RequiresReanalysis]!.GetValue<bool>()); Assert.Equal("2330", board[AgentBlackboardKeys.Ticker]!.GetValue<string>()); Assert.Equal("關鍵數字改變", board[AgentBlackboardKeys.ReanalysisReasons]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("wrong-user", "Evidence remediation run not found.")]
    [InlineData("wrong-workflow", "Source run is not an EvidenceRemediation workflow.")]
    [InlineData("not-succeeded", "Evidence remediation run has not succeeded.")]
    [InlineData("invalid-json", "Evidence remediation output is invalid.")]
    [InlineData("not-required", "Evidence remediation does not require reanalysis.")]
    public async Task Load_InvalidSource_RejectsClearly(string scenario, string expected)
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = Source(userId);
        if (scenario == "wrong-workflow") source.WorkflowType = AgentWorkflowTypes.CriticReview;
        if (scenario == "not-succeeded") source.Status = AgentRunStatuses.Failed;
        if (scenario == "invalid-json") source.OutputJson = "not-json";
        if (scenario == "not-required") { var output = JsonSerializer.Deserialize<EvidenceRemediationOutput>(source.OutputJson!, AgentNodeJson.SerializerOptions)!; source.OutputJson = AgentNodeJson.Serialize(output with { RequiresReanalysis = false }); }
        db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceReanalysisWorkflowDefinitionProvider().CreateRun(scenario == "wrong-user" ? Guid.NewGuid() : userId, source.Id);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(0), (_, _, _, _, _) => { })));
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public async Task Validate_MissingReasons_Rejects()
    {
        await using var db = CreateDb(); var run = new EvidenceReanalysisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.RequiresReanalysis] = true; board[AgentBlackboardKeys.ReanalysisReasons] = new JsonArray(); board[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializer.SerializeToNode(Packet(), AgentNodeJson.SerializerOptions); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new ValidateReanalysisRequestNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(1), (_, _, _, _, _) => { }))); Assert.Equal("Reanalysis reasons are missing.", exception.Message);
    }

    [Fact]
    public async Task InvestmentAgent_ValidatesStructuredOutputAndTelemetry()
    {
        var chat = new FakeChat("{\"reanalyzedAnswer\":\"新分析 [1]\",\"analysisChangeSummary\":\"修正成長率\",\"changedClaimIds\":[\"claim-1\"],\"keyConclusionChanges\":[\"風險提高\"]}"); var result = await new LlmInvestmentReanalysisAgent(chat).ReanalyzeAsync(Context());
        Assert.Equal("新分析 [1]", result.Draft.ReanalyzedAnswer); Assert.Equal("InvestmentReanalysisAgent", result.AgentIdentity); Assert.Equal("actual-model", result.Model); Assert.Equal(17, result.PromptTokens + result.CompletionTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"reanalyzedAnswer\":\"新分析 [2]\",\"analysisChangeSummary\":\"x\",\"changedClaimIds\":[\"claim-1\"],\"keyConclusionChanges\":[]}")]
    [InlineData("{\"reanalyzedAnswer\":\"新分析 [1]\",\"analysisChangeSummary\":\"x\",\"changedClaimIds\":[\"unknown\"],\"keyConclusionChanges\":[]}")]
    public async Task InvestmentAgent_InvalidOutput_Throws(string response)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => new LlmInvestmentReanalysisAgent(new FakeChat(response)).ReanalyzeAsync(Context()));
    }

    [Fact]
    public async Task SevenHandlers_ProduceFinalOutputWithoutLoop()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = Source(userId); var run = new EvidenceReanalysisWorkflowDefinitionProvider().CreateRun(userId, source.Id); db.AgentRuns.AddRange(source, run); await db.SaveChangesAsync(); var events = new List<(string Type, object? Payload)>(); AgentRunEventWriter writer = (_, _, type, _, payload) => events.Add((type, payload));
        await new LoadEvidenceRemediationNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(0), writer)); await new ValidateReanalysisRequestNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(1), writer)); await new BuildAnalysisContextNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(2), writer)); await new ReanalyzeAnswerNodeHandler(new FakeAnalysisAgent()).ExecuteAsync(new(db, run, run.Nodes.ElementAt(3), writer));
        await new CritiqueReanalysisNodeHandler(new DeterministicCriticReviewAgent(), [new EvidenceReanalysisPolicyEvaluator()]).ExecuteAsync(new(db, run, run.Nodes.ElementAt(4), writer)); await new ReviseReanalysisNodeHandler(new DeterministicDraftRevisionAgent()).ExecuteAsync(new(db, run, run.Nodes.ElementAt(5), writer)); await new FinalizeReanalysisNodeHandler().ExecuteAsync(new(db, run, run.Nodes.ElementAt(6), writer));
        var output = JsonSerializer.Deserialize<EvidenceReanalysisOutput>(run.OutputJson!, AgentNodeJson.SerializerOptions)!; Assert.Equal("重新分析 [1]", output.FinalAnswer); Assert.False(output.RequiresMoreEvidence); Assert.Equal("AcceptAnswer", output.RecommendedNextAction); Assert.Single(output.Citations); Assert.Equal("Complete", output.AnswerQualityStatus); Assert.NotNull(AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.AnswerQualityValidation]); Assert.Contains(events, x => x.Type == AgentEventTypes.SupervisorDecision); Assert.Contains(db.AgentToolCalls.Local, x => x.ToolName == "investmentReanalysisLLM");
    }

    private static AgentRun Source(Guid userId)
    {
        var packet = Packet(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, Guid.NewGuid()); run.Status = AgentRunStatuses.Succeeded; var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Ticker] = "2330"; board[AgentBlackboardKeys.Question] = "展望？"; board[AgentBlackboardKeys.Answer] = "原回答"; board[AgentBlackboardKeys.CriticFindings] = new JsonArray(); board[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializer.SerializeToNode(packet, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.EvidenceValidationResults] = JsonSerializer.SerializeToNode(new EvidenceValidationResult("Supported", packet.Claims, [], true, ["關鍵數字改變"]), AgentNodeJson.SerializerOptions); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); run.OutputJson = AgentNodeJson.Serialize(new EvidenceRemediationOutput("原回答", "補證據修正版", "已修訂", "Supported", packet.Evidence, [], true, ["關鍵數字改變"])); return run;
    }
    private static RemediatedEvidencePacket Packet() => new("Supported", [new("claim-1", "營收成長 20%", "Supported", [1], [])], [new(1, "LocalDocument", "年報", "AnnualReport", null, "營收成長 20%", .9)], [], true, ["關鍵數字改變"]);
    private static InvestmentReanalysisContext Context() => new("2330", "展望？", "原回答", "補證據修正版", [new("claim-1", "營收成長 20%", "Supported", [1])], [new(1, "LocalDocument", "年報", "AnnualReport", null, "營收成長 20%", .9)], ["關鍵數字改變"]);
    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class FakeChat(string content) : IChatCompletionService { public string Provider => "test"; public string Model => "configured"; public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ChatCompletionResult(content, "actual-model", 10, 7)); }
    private sealed class FakeAnalysisAgent : IInvestmentReanalysisAgent { public Task<InvestmentReanalysisAgentResult> ReanalyzeAsync(InvestmentReanalysisContext context, CancellationToken cancellationToken = default) => Task.FromResult(new InvestmentReanalysisAgentResult(new("重新分析 [1]", "已重算", ["claim-1"], ["結論更新"]), "InvestmentReanalysisAgent", "test", "test-model", 10, 5, null)); }
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options) { protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever(); } }
}
