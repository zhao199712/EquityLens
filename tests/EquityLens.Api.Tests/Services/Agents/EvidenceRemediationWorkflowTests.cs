using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class EvidenceRemediationWorkflowTests
{
    [Fact]
    public async Task LoadContext_UsesCriticSnapshotWithoutReadingResearchRun()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        await new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); Assert.Equal("2330", board[AgentBlackboardKeys.Ticker]!.GetValue<string>()); Assert.Equal("原回答", board[AgentBlackboardKeys.Answer]!.GetValue<string>()); Assert.Equal(source.Id, board[AgentBlackboardKeys.CriticReviewRunId]!.GetValue<Guid>());
    }

    [Fact]
    public async Task LoadContext_OutputJsonOverridesStaleBlackboardReview()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson); sourceBoard[AgentBlackboardKeys.CriticReview]![CriticReviewFields.RequiresMoreEvidence] = false; source.BlackboardJson = sourceBoard.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        await new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); Assert.True(board[AgentBlackboardKeys.CriticReview]![CriticReviewFields.RequiresMoreEvidence]!.GetValue<bool>()); Assert.Single(board[AgentBlackboardKeys.CriticFindings]!.AsArray());
    }

    [Fact]
    public async Task LoadContext_MissingOutputJsonFallsBackToFinalOutput()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson); sourceBoard[AgentBlackboardKeys.FinalOutput] = sourceBoard[AgentBlackboardKeys.CriticReview]!.DeepClone(); sourceBoard[AgentBlackboardKeys.CriticReview]![CriticReviewFields.RequiresMoreEvidence] = false; source.OutputJson = null; source.BlackboardJson = sourceBoard.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        await new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        Assert.True(AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.CriticReview]![CriticReviewFields.RequiresMoreEvidence]!.GetValue<bool>());
    }

    [Fact]
    public async Task LoadContext_InvalidOutputJson_IsRejectedWithoutFallback()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); source.OutputJson = "not-json"; db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { })));

        Assert.Equal("Critic review output is invalid.", exception.Message);
    }

    [Fact]
    public async Task LoadContext_OutputThatDoesNotRequireEvidence_IsRejected()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); var output = JsonNode.Parse(source.OutputJson!)!.AsObject(); output[CriticReviewFields.RequiresMoreEvidence] = false; source.OutputJson = output.ToJsonString(); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { })));

        Assert.Equal("Critic review does not require more evidence.", exception.Message);
    }

    [Theory]
    [InlineData("OtherWorkflow", AgentRunStatuses.Succeeded, "Source run is not a CriticReview workflow.")]
    [InlineData(AgentWorkflowTypes.CriticReview, AgentRunStatuses.Failed, "Critic review run has not succeeded.")]
    public async Task LoadContext_InvalidSourceWorkflowOrStatus_IsRejected(string workflowType, string status, string expectedMessage)
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); source.WorkflowType = workflowType; source.Status = status; db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { })));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task LoadContext_MissingFinalReview_IsRejected()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var source = CriticSource(userId); var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson); sourceBoard[AgentBlackboardKeys.FinalOutput] = null; sourceBoard[AgentBlackboardKeys.CriticReview] = null; source.OutputJson = null; source.BlackboardJson = sourceBoard.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { })));

        Assert.Equal("Critic review output is missing.", exception.Message);
    }

    [Fact]
    public async Task LoadContext_DifferentUserCriticRun_IsRejected()
    {
        await using var db = CreateDb(); var source = CriticSource(Guid.NewGuid()); db.AgentRuns.Add(source); await db.SaveChangesAsync(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), source.Id); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.LoadContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new LoadEvidenceRemediationContextNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { })));

        Assert.Equal("Critic review run not found.", exception.Message);
    }

    [Fact]
    public async Task RetrieveEvidence_LocalInsufficient_UsesSingleWebFallback()
    {
        await using var db = CreateDb(); var provider = new EvidenceRemediationWorkflowDefinitionProvider(); var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x.Iteration == 1);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Ticker] = "2330"; board[AgentBlackboardKeys.RetrievalPlan] = JsonSerializerNode(new ResearchRetrievalStrategy("Auto", [new ResearchRetrievalSearch(null, "Primary", "台積電風險", 5, "test")])); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();
        var web = new FakeWebRetriever(); var handler = new RetrieveRemediationEvidenceNodeHandler(new FakeDocumentRetriever([]), web);

        await handler.ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        Assert.Equal(1, web.CallCount); var result = AgentNodeJson.ParseBlackboard(run.BlackboardJson); Assert.Single(result[AgentBlackboardKeys.RetrievedEvidence]!.AsArray()); Assert.Contains(run.ToolCalls, x => x.ToolName == "documentRetrieval"); Assert.Contains(run.ToolCalls, x => x.ToolName == "webSearch");
    }

    [Fact]
    public async Task RetrieveEvidence_LocalSufficient_DoesNotUseWeb()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x.Iteration == 1); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Ticker] = "2330"; board[AgentBlackboardKeys.RetrievalPlan] = JsonSerializerNode(new ResearchRetrievalStrategy("Auto", [new ResearchRetrievalSearch(null, "Primary", "query", 5, "test")])); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync(); var web = new FakeWebRetriever();

        await new RetrieveRemediationEvidenceNodeHandler(new FakeDocumentRetriever([Chunk("A"), Chunk("B")]), web).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        Assert.Equal(0, web.CallCount);
    }

    [Fact]
    public async Task NumericMismatch_ProducesInsufficientEvidence_AndSkipsRevisionAgent()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var validate = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.ValidateMappings && x.Iteration == 1); var draft = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.DraftRevision); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "營收？"; board[AgentBlackboardKeys.Answer] = "2026 年營收成長 20%。"; board[AgentBlackboardKeys.ExtractedClaims] = JsonSerializerNode(new[] { new EvidenceClaim("claim-1", "2026 年營收成長 20%。", ["2026", "20%"]) }); board[AgentBlackboardKeys.RetrievedEvidence] = JsonSerializerNode(new[] { new RemediationEvidenceItem(1, "LocalDocument", "年報", "AnnualReport", null, "2025 年營收成長 10%。", .9) }); board[AgentBlackboardKeys.ClaimSupportAssessments] = JsonSerializerNode(new[] { new ClaimSupportAssessment("claim-1", "Supported", [1], "looks related") }); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var context = new AgentNodeExecutionContext(db, run, validate, (_, _, _, _, _) => { }); await new ValidateEvidenceMappingsNodeHandler().ExecuteAsync(context); board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var validation = EvidenceRemediationBoardForTest.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults); var evidence = EvidenceRemediationBoardForTest.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); board[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializerNode(new RemediatedEvidencePacket(validation.EvidenceStatus, validation.Claims, evidence, validation.UnresolvedClaimIds)); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); var agent = new RecordingRevisionAgent();

        await new DraftEvidenceBackedRevisionNodeHandler(agent).ExecuteAsync(new AgentNodeExecutionContext(db, run, draft, (_, _, _, _, _) => { }));

        Assert.Equal("InsufficientEvidence", validation.EvidenceStatus); Assert.Equal(0, agent.CallCount); Assert.Equal("2026 年營收成長 20%。", AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.RevisedAnswer]!.GetValue<string>());
    }

    private static JsonNode JsonSerializerNode<T>(T value) => System.Text.Json.JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions)!;
    private static AgentRun CriticSource(Guid userId)
    {
        var board = AgentBlackboardContracts.CreateInitialCriticReviewBlackboard(Guid.NewGuid()); board[AgentBlackboardKeys.Ticker] = "2330"; board[AgentBlackboardKeys.Question] = "問題"; board[AgentBlackboardKeys.Answer] = "原回答"; board[AgentBlackboardKeys.CriticFindings] = new JsonArray(AgentBlackboardContracts.CreateFinding("High", "WeakCitation", "引用不足", "補充證據")); board[AgentBlackboardKeys.CriticReview] = new JsonObject { [CriticReviewFields.RequiresMoreEvidence] = true, [CriticReviewFields.Findings] = board[AgentBlackboardKeys.CriticFindings]!.DeepClone() };
        return new AgentRun { Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.CriticReview, AgentType = AgentTypes.Critic, Status = AgentRunStatuses.Succeeded, InputJson = "{}", BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions), WorkflowDefinitionJson = "{\"nodes\":[],\"edges\":[]}", OutputJson = board[AgentBlackboardKeys.CriticReview]!.ToJsonString(), CreatedAtUtc = DateTime.UtcNow };
    }
    private static RetrievedDocumentChunk Chunk(string content) => new(new DocumentSearchResult(Guid.NewGuid(), Guid.NewGuid(), "文件", "AnnualReport", null, 1, 1, null, content, .1, .9, null, "2330", "TWSE", "台積電"), "Primary", "search-1", "query");
    private static TestDb CreateDb() { var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options; return new TestDb(options); }

    private sealed class FakeDocumentRetriever(IReadOnlyList<RetrievedDocumentChunk> results) : IDocumentRetriever
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveAsync(ResearchRetrievalStrategy strategy, string ticker, CancellationToken cancellationToken = default) => Task.FromResult(results);
        public IReadOnlyList<RetrievedDocumentChunk> GetCandidatesForRerank(IReadOnlyList<RetrievedDocumentChunk> chunks, int targetCount) => chunks;
    }
    private sealed class FakeWebRetriever : IWebRetriever
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveWebAsync(string query, int count, string? freshness, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>([Chunk("Web evidence") with { SourceType = CitationSourceType.Web, Url = "https://example.test" }]); }
    }
    private sealed class RecordingRevisionAgent : IEvidenceBackedRevisionAgent
    {
        public int CallCount { get; private set; }
        public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(new EvidenceBackedRevisionResult("changed", "changed")); }
    }
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever(); }
    }
    private static class EvidenceRemediationBoardForTest
    {
        public static T Required<T>(JsonObject board, string key) => System.Text.Json.JsonSerializer.Deserialize<T>(board[key]!.ToJsonString(), AgentNodeJson.SerializerOptions)!;
    }
}
