using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class EvidenceAssessorTests
{
    [Theory]
    [InlineData("Supported", "None")]
    [InlineData("PartiallySupported", "WordingOnly")]
    [InlineData("Contradicted", "Material")]
    [InlineData("Unverifiable", "None")]
    public async Task AssessAsync_ValidStructuredResponse_ReturnsDedicatedAgentTelemetry(string status, string impact)
    {
        var json = $$"""{"assessments":[{"claimId":"claim-1","status":"{{status}}","evidenceIndexes":[1],"reason":"reason","confidence":0.9,"analysisImpact":"{{impact}}","impactReason":"impact"}]}""";
        var result = await new LlmEvidenceAssessor(new FakeChat(json)).AssessAsync(Input());

        var assessment = Assert.Single(result.Assessments);
        Assert.Equal(status, assessment.Status); Assert.Equal(impact, assessment.AnalysisImpact);
        Assert.Equal("EvidenceAssessor", result.AgentIdentity); Assert.Equal("test-provider", result.Provider); Assert.Equal("actual-model", result.Model);
        Assert.Equal(12, result.PromptTokens); Assert.Equal(7, result.CompletionTokens); Assert.Null(result.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"assessments\":[]}")]
    public async Task AssessAsync_InvalidOrEmptyOutput_Throws(string content)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => new LlmEvidenceAssessor(new FakeChat(content)).AssessAsync(Input()));
    }

    [Fact]
    public async Task AssessAsync_PreservesQuestionRelevanceAndAnswerabilityEffect()
    {
        var json = """{"assessments":[{"claimId":"claim-1","status":"PartiallySupported","evidenceIndexes":[1],"reason":"supports direction","confidence":0.8,"analysisImpact":"Material","impactReason":"enables analysis","questionRelevance":"Core","answerabilityEffect":"EnablesBoundedAnswer"}]}""";

        var assessment = Assert.Single((await new LlmEvidenceAssessor(new FakeChat(json)).AssessAsync(Input())).Assessments);

        Assert.Equal("Core", assessment.QuestionRelevance); Assert.Equal("EnablesBoundedAnswer", assessment.AnswerabilityEffect);
    }

    [Fact]
    public async Task Validate_InvalidQuestionRelevance_IsRejected()
    {
        var validation = await ValidateAsync(new("claim-1", "Supported", [1], "bad enum", .9, "None", "", "Important", "NoChange"), "2026 營收成長 20%");

        Assert.Contains(Assert.Single(validation.Claims).ValidationErrors, x => x.Contains("Question relevance"));
    }

    [Fact]
    public async Task Validate_MaterialSupportedEvidence_RequiresReanalysis()
    {
        var validation = await ValidateAsync(new("claim-1", "Supported", [1], "new guidance", .95, "Material", "成長率改變估值假設"), "2026 營收成長 20%");
        Assert.True(validation.RequiresReanalysis); Assert.Contains("成長率改變估值假設", validation.ReanalysisReasons!);
    }

    [Fact]
    public async Task Validate_WordingOnlyEvidence_DoesNotRequireReanalysis()
    {
        var validation = await ValidateAsync(new("claim-1", "Supported", [1], "citation only", .9, "WordingOnly", "補引用"), "2026 營收成長 20%");
        Assert.False(validation.RequiresReanalysis); Assert.Empty(validation.ReanalysisReasons!);
    }

    [Fact]
    public async Task Validate_ValidContradiction_RequiresReanalysis()
    {
        var validation = await ValidateAsync(new("claim-1", "Contradicted", [1], "contradiction", .9, "None", "新證據與原答案矛盾"), "2026 營收成長 20%");
        Assert.True(validation.RequiresReanalysis); Assert.Equal("Contradicted", Assert.Single(validation.Claims).Status);
    }

    [Fact]
    public async Task Validate_MaterialWithInvalidEvidenceIndex_IsRejected()
    {
        var validation = await ValidateAsync(new("claim-1", "Supported", [2], "bad mapping", .9, "Material", "不應採信"), "2026 營收成長 20%");
        Assert.False(validation.RequiresReanalysis); Assert.Contains(Assert.Single(validation.Claims).ValidationErrors, x => x.Contains("out of range"));
    }

    [Fact]
    public async Task AssessNode_StoresDedicatedAgentToolTelemetry()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.AssessSupport && x.Iteration == 1);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "營收？"; board[AgentBlackboardKeys.Answer] = "原回答"; board[AgentBlackboardKeys.ExtractedClaims] = JsonSerializer.SerializeToNode(new[] { new EvidenceClaim("claim-1", "claim", Array.Empty<string>()) }, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.RetrievedEvidence] = JsonSerializer.SerializeToNode(new[] { new RemediationEvidenceItem(1, "LocalDocument", "年報", "AnnualReport", null, "evidence", .9) }, AgentNodeJson.SerializerOptions); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();

        await new AssessClaimSupportNodeHandler(new FakeAssessor()).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var call = Assert.Single(db.AgentToolCalls.Local, x => x.AgentRunNodeId == node.Id);
        Assert.Equal("evidenceAssessorLLM", call.ToolName); Assert.Contains("EvidenceAssessor", call.ArgumentsJson); Assert.Contains("actual-model", call.ResultJson);
    }

    [Fact]
    public async Task RouteAndFinalize_PreserveReanalysisDecisionInBlackboardEventAndOutput()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var route = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.Route && x.Iteration == 1); var finalize = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.Finalize);
        var validatedClaim = new ValidatedClaimSupport("claim-1", "claim", "Supported", [1], []); var validation = new EvidenceValidationResult("Supported", [validatedClaim], [], true, ["關鍵展望已改變"]); var packet = new RemediatedEvidencePacket("Supported", [validatedClaim], [new(1, "LocalDocument", "年報", "AnnualReport", null, "evidence", .9)], [], true, ["關鍵展望已改變"]);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Answer] = "原回答"; board[AgentBlackboardKeys.RevisedAnswer] = "修正版"; board[AgentBlackboardKeys.RevisionSummary] = "已修訂；建議重新分析"; board[AgentBlackboardKeys.EvidenceValidationResults] = JsonSerializer.SerializeToNode(validation, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializer.SerializeToNode(packet, AgentNodeJson.SerializerOptions); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();

        object? supervisorPayload = null;
        await new RouteEvidenceRemediationNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, route, (_, _, eventType, _, payload) => { if (eventType == AgentEventTypes.SupervisorDecision) supervisorPayload = payload; }));
        await new FinalizeEvidenceRemediationNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, finalize, (_, _, _, _, _) => { }));

        board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); Assert.True(board[AgentBlackboardKeys.RequiresReanalysis]!.GetValue<bool>()); Assert.Equal("關鍵展望已改變", board[AgentBlackboardKeys.ReanalysisReasons]![0]!.GetValue<string>());
        var output = JsonSerializer.Deserialize<EvidenceRemediationOutput>(run.OutputJson!, AgentNodeJson.SerializerOptions)!; Assert.True(output.RequiresReanalysis); Assert.Contains("關鍵展望已改變", output.ReanalysisReasons!);
        Assert.NotNull(supervisorPayload); Assert.Contains("requiresReanalysis", AgentNodeJson.Serialize(supervisorPayload));
    }

    private static EvidenceAssessmentInput Input() => new("營收展望？", "原回答", [new("claim-1", "claim", [])], [new(1, "LocalDocument", "年報", "AnnualReport", null, "evidence", .9)], []);

    private static async Task<EvidenceValidationResult> ValidateAsync(ClaimSupportAssessment assessment, string evidenceText)
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.ValidateMappings && x.Iteration == 1);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.ExtractedClaims] = JsonSerializer.SerializeToNode(new[] { new EvidenceClaim("claim-1", "2026 營收成長 20%", ["2026", "20%"]) }, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.RetrievedEvidence] = JsonSerializer.SerializeToNode(new[] { new RemediationEvidenceItem(1, "LocalDocument", "年報", "AnnualReport", null, evidenceText, .9) }, AgentNodeJson.SerializerOptions); board[AgentBlackboardKeys.ClaimSupportAssessments] = JsonSerializer.SerializeToNode(new[] { assessment }, AgentNodeJson.SerializerOptions); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();
        await new ValidateEvidenceMappingsNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));
        board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); return JsonSerializer.Deserialize<EvidenceValidationResult>(board[AgentBlackboardKeys.EvidenceValidationResults]!.ToJsonString(), AgentNodeJson.SerializerOptions)!;
    }

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeChat(string content) : IChatCompletionService
    {
        public string Provider => "test-provider"; public string Model => "configured-model";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ChatCompletionResult(content, "actual-model", 12, 7));
    }

    private sealed class FakeAssessor : IEvidenceAssessor
    {
        public Task<EvidenceAssessorResult> AssessAsync(EvidenceAssessmentInput input, CancellationToken cancellationToken = default) => Task.FromResult(new EvidenceAssessorResult([new("claim-1", "Supported", [1], "supported", .9, "None", "")], "EvidenceAssessor", "test-provider", "actual-model", 12, 7, null));
    }

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever(); }
    }
}
