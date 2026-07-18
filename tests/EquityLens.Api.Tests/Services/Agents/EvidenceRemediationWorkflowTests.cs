using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
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
    public async Task ExtractClaims_EmptyAgentResult_CreatesInvestigationClaim()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.ExtractClaims);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "未來資本支出會壓縮現金流嗎？"; board[AgentBlackboardKeys.Answer] = "目前資料不足。"; board[AgentBlackboardKeys.CriticFindings] = new JsonArray(AgentBlackboardContracts.CreateFinding("High", "InsufficientEvidence", "需要未來指引", "補查法說")); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();

        await new ExtractAnswerClaimsNodeHandler(new EmptyClaimAgent()).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        var claims = EvidenceRemediationBoardForTest.Required<List<EvidenceClaim>>(AgentNodeJson.ParseBlackboard(run.BlackboardJson), AgentBlackboardKeys.ExtractedClaims);
        Assert.Equal(6, claims.Count); Assert.All(claims, claim => Assert.Equal(EvidenceClaimKinds.InvestigationClaim, claim.Kind)); Assert.Contains(claims, claim => claim.ResearchDimension == "FCF impact"); Assert.Contains(claims, claim => claim.ResearchDimension == "Shareholder returns"); Assert.DoesNotContain(claims, claim => claim.ClaimType == EvidenceClaimTypes.Answerability);
    }

    [Fact]
    public async Task LlmClaimExtraction_EmptyClaims_DefersFallbackToClaimSetGate()
    {
        var agent = new LlmEvidenceRemediationAgent(new EmptyClaimsChat());
        var claims = await agent.ExtractAsync(new("未來資本支出？", "目前資料不足。", []));
        Assert.Empty(claims);
    }

    [Fact]
    public void ClaimSetValidator_RejectsCoarseWeakRecoverAnswerClaims()
    {
        var claims = new[]
        {
            new EvidenceClaim("claim-1", "歷史資本支出可作為預測未來趨勢的基礎。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Factual, "Financial Analysis"),
            new EvidenceClaim("claim-2", "資本支出增加會減少自由現金流。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Mechanism, "Capital Allocation"),
            new EvidenceClaim("claim-3", "自由現金流下降可能壓縮股東回報。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Judgment, "Shareholder Returns")
        };

        var result = new ClaimSetValidator().Validate(new("台積電未來兩年的資本支出是否會壓縮自由現金流與股東回報？", "資料不足。", [], InvestigationModes.RecoverAnswer, claims));

        Assert.False(result.IsValid); Assert.Contains(result.Errors, x => x.Contains("weak research propositions")); Assert.Contains("Depreciation impact", result.MissingDimensions); Assert.True(result.Coverage < 1);
    }

    [Fact]
    public void ClaimSetValidator_DeterministicCapexDecomposition_CoversSixDimensions()
    {
        var input = new ClaimExtractionInput("台積電未來兩年的資本支出是否會壓縮自由現金流與股東回報？", "資料不足。", []);
        var claims = LlmEvidenceRemediationAgent.CreateInvestigationFallback(input);

        var result = new ClaimSetValidator().Validate(new(input.Question, input.Answer, input.CriticFindings, InvestigationModes.RecoverAnswer, claims));

        Assert.True(result.IsValid); Assert.Equal(1, result.Coverage); Assert.Equal(6, result.RequiredDimensions.Count); Assert.Empty(result.MissingDimensions);
    }

    [Fact]
    public async Task ExtractClaims_InvalidInitialSet_RepairsOnceAndPersistsAuditData()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var node = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.ExtractClaims); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "台積電未來兩年的資本支出是否會壓縮自由現金流與股東回報？"; board[AgentBlackboardKeys.Answer] = "目前資料不足。"; run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync(); var agent = new RepairingClaimAgent();

        await new ExtractAnswerClaimsNodeHandler(agent, new ClaimSetValidator()).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var validation = EvidenceRemediationBoardForTest.Required<ClaimSetValidationResult>(board, AgentBlackboardKeys.ClaimSetValidation);
        Assert.Equal(2, agent.CallCount); Assert.Equal("LlmRepair", validation.Resolution); Assert.Equal(2, board[AgentBlackboardKeys.ClaimRepairHistory]!.AsArray().Count); Assert.Equal(6, board[AgentBlackboardKeys.RequiredResearchDimensions]!.AsArray().Count); Assert.Contains(run.ToolCalls, x => x.ToolName == "claimExtractionRepairLLM");
    }

    [Fact]
    public async Task ValidateMappings_PartialAndAnswerabilityClaims_DoNotCompleteResearchGoal()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var validate = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.ValidateMappings && x.Iteration == 1); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.ExtractedClaims] = JsonSerializerNode(new[] { new EvidenceClaim("meta", "目前資料不足。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Answerability, "Answerability"), new EvidenceClaim("domain", "資本支出可能壓低短期自由現金流。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Mechanism, "FCF impact") });
        board[AgentBlackboardKeys.RetrievedEvidence] = JsonSerializerNode(new[] { new RemediationEvidenceItem(1, "Web", "Capex", "WebSearch", "https://example.test", "Higher capital expenditure may reduce near-term free cash flow.", .8) });
        board[AgentBlackboardKeys.ClaimSupportAssessments] = JsonSerializerNode(new[] { new ClaimSupportAssessment("meta", "Supported", [1], "The old answer lacked data."), new ClaimSupportAssessment("domain", "PartiallySupported", [1], "Direction is supported but magnitude is unknown.") });
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        await new ValidateEvidenceMappingsNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, validate, (_, _, _, _, _) => { }));

        var result = EvidenceRemediationBoardForTest.Required<EvidenceValidationResult>(AgentNodeJson.ParseBlackboard(run.BlackboardJson), AgentBlackboardKeys.EvidenceValidationResults);
        Assert.Equal("PartiallySupported", result.EvidenceStatus); Assert.Single(result.UnresolvedClaimIds); Assert.Equal("domain", result.UnresolvedClaimIds[0]);
    }

    [Fact]
    public async Task RecoverAnswer_WithValidatedEvidence_RejectsShortAbstention()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var draft = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.DraftRevision); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "未來資本支出會壓縮自由現金流嗎？"; board[AgentBlackboardKeys.Answer] = "目前資料不足，無法回答。"; board[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializerNode(new RemediatedEvidencePacket("Supported", [new ValidatedClaimSupport("claim-1", "資本支出增加會壓低短期自由現金流。", "Supported", [1], [])], [new RemediationEvidenceItem(1, "Web", "Capex", "WebSearch", "https://example.test", "Higher capex may reduce near-term FCF.", .8)], [])); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new DraftEvidenceBackedRevisionNodeHandler(new FixedRevisionAgent("目前資料不足，無法回答。[1]")).ExecuteAsync(new AgentNodeExecutionContext(db, run, draft, (_, _, _, _, _) => { })));

        Assert.Contains("still primarily abstains", exception.Message);
    }

    [Theory]
    [InlineData(1, "PartiallySupportedNeedsRetrieval")]
    [InlineData(2, "PartiallySupportedBudgetExhausted")]
    public async Task DynamicRoute_PreservesPartialEvidenceBudgetState(int iteration, string expected)
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); run.WorkflowType = AgentWorkflowTypes.ResearchQualityReview; var route = run.Nodes.Single(x => x.NodeType == EvidenceRemediationNodeTypes.Route && x.Iteration == iteration); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.EvidenceValidationResults] = JsonSerializerNode(new EvidenceValidationResult("PartiallySupported", [new("claim-1", "claim", "PartiallySupported", [1], [])], ["claim-1"])); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        await new RouteEvidenceRemediationNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, route, (_, _, _, _, _) => { }));

        Assert.Equal(expected, AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.RouteDecision]!.GetValue<string>());
    }

    [Fact]
    public async Task RetrieveWebEvidence_AppendsDeduplicatedBraveResultsAndTelemetry()
    {
        await using var db = CreateDb(); var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "retrieveWebEvidence:1", TemplateNodeKey = "retrieve-web-evidence", NodeType = EvidenceRemediationNodeTypes.RetrieveWebEvidence, Iteration = 1, Status = AgentNodeStatuses.Running, InputJson = new JsonObject { ["searchIntents"] = new JsonArray(new JsonObject { ["topic"] = "TSMC capex", ["targetClaims"] = new JsonArray("claim-1"), ["preferredSourceRoles"] = new JsonArray("Primary"), ["freshness"] = "month", ["topK"] = 5 }) }.ToJsonString() };
        run.Nodes.Add(node); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Question] = "TSMC capex?"; board[AgentBlackboardKeys.ExtractedClaims] = JsonSerializerNode(new[] { new EvidenceClaim("claim-1", "TSMC capex guidance", []) }); board[AgentBlackboardKeys.RetrievedEvidence] = JsonSerializerNode(new[] { new RemediationEvidenceItem(1, "Web", "Existing", "WebSearch", "https://example.test", "same", .5) }); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); db.AgentRuns.Add(run); await db.SaveChangesAsync();

        await new RetrieveWebEvidenceNodeHandler(new FakeWebRetriever()).ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var evidence = EvidenceRemediationBoardForTest.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence);
        Assert.Single(evidence); Assert.Equal("Brave", evidence[0].Provider); Assert.Contains(run.ToolCalls, x => x.ToolName == "braveWebSearch" && !x.ArgumentsJson.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)); Assert.Contains("Brave", board[AgentBlackboardKeys.RetrievalHistory]!.ToJsonString(), StringComparison.Ordinal);
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
    private sealed class EmptyClaimAgent : IClaimExtractionAgent
    {
        public Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EvidenceClaim>>([]);
    }
    private sealed class RepairingClaimAgent : IClaimExtractionAgent
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CallCount == 1
                ? (IReadOnlyList<EvidenceClaim>)[new("weak", "歷史資料可作為參考。", [], EvidenceClaimKinds.InvestigationClaim, EvidenceClaimTypes.Factual, "Financial Analysis")]
                : LlmEvidenceRemediationAgent.CreateInvestigationFallback(input));
        }
    }
    private sealed class EmptyClaimsChat : IChatCompletionService
    {
        public string Provider => "test"; public string Model => "test";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ChatCompletionResult("{\"claims\":[]}", Model, 1, 1));
    }
    private sealed class RecordingRevisionAgent : IEvidenceBackedRevisionAgent
    {
        public int CallCount { get; private set; }
        public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(new EvidenceBackedRevisionResult("changed", "changed")); }
    }
    private sealed class FixedRevisionAgent(string answer) : IEvidenceBackedRevisionAgent
    {
        public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) => Task.FromResult(new EvidenceBackedRevisionResult(answer, "test"));
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
