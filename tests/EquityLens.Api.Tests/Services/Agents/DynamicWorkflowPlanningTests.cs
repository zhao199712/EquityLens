using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class DynamicWorkflowPlanningTests
{
    [Fact]
    public void RoutedConferenceCallSkill_ConstrainsInitialPlannerAndValidatorToLeadCapabilities()
    {
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new("2454", "聯發科法說會相較上季改變了什麼？"));
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.ResearchIntent] = new JsonObject();
        board[AgentBlackboardKeys.LeadSkill] = "conference-call-takeaways";
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var catalog = new WorkflowSkillCatalog();
        var lead = catalog.Skills.Single(x => x.Id == "conference-call-takeaways");
        var capabilities = new NodeCapabilityRegistry();
        var allowed = lead.Capabilities.ToHashSet(StringComparer.Ordinal);
        var context = new WorkflowPlanningContext(
            run.Id,
            run.OrchestrationVersion,
            DynamicPlanningTriggers.ResearchContextReady,
            board,
            [ResearchInvestigationNodeTypes.Validate, ResearchInvestigationNodeTypes.DetectIntent],
            [lead],
            capabilities.Capabilities.Where(x => allowed.Contains(x.Id)).ToList(),
            0,
            0);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);
        var validated = new DynamicPlanValidator(capabilities, new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);

        Assert.Equal(["conference-call-takeaways"], proposal.SelectedSkills);
        Assert.All(validated.Actions, action => Assert.Contains(action.Capability, lead.Capabilities));
        Assert.Equal(ResearchQualityReviewNodeTypes.FinalizeCriticReport, validated.Actions[^1].NodeType);
    }

    [Fact]
    public void RoutedGenericResearchSkill_AllowsSingleAssetMathCapability()
    {
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(
            Guid.NewGuid(), Guid.NewGuid(), new("2454", "計算聯發科夏普值"));
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.ResearchIntent] = new JsonObject();
        board[AgentBlackboardKeys.LeadSkill] = "research-investigation";
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var catalog = new WorkflowSkillCatalog();
        var lead = catalog.Skills.Single(x => x.Id == "research-investigation");
        var capabilities = new NodeCapabilityRegistry();
        var allowed = lead.Capabilities.ToHashSet(StringComparer.Ordinal);
        var context = new WorkflowPlanningContext(
            run.Id, run.OrchestrationVersion, DynamicPlanningTriggers.ResearchContextReady, board,
            [ResearchInvestigationNodeTypes.Validate, ResearchInvestigationNodeTypes.DetectIntent],
            [lead], capabilities.Capabilities.Where(x => allowed.Contains(x.Id)).ToList(), 0, 0);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);
        var validated = new DynamicPlanValidator(capabilities, new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);

        Assert.Contains(validated.Actions, x => x.Capability == "prepare-portfolio-risk-math-inputs");
        Assert.Contains(validated.Actions, x => x.Capability == "calculate-sharpe-ratio");
    }

    [Theory]
    [InlineData("為甚麼台積電7/17跌這麼多")]
    [InlineData("台積電今天為什麼跌")]
    public void DeterministicPlanner_FreshMarketQuestion_MaterializesWebResearchBranch(string question)
    {
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid(), new("2330", question));
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.ResearchIntent] = new JsonObject { ["selected"] = 3 };
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, DynamicPlanningTriggers.ResearchContextReady, board, [ResearchInvestigationNodeTypes.Validate, ResearchInvestigationNodeTypes.DetectIntent], new WorkflowSkillCatalog().Skills, new NodeCapabilityRegistry().Capabilities, 0, 0);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);
        var validated = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);

        Assert.Contains("current-market-event-investigation", proposal.SelectedSkills);
        Assert.Contains(validated.Actions, x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb);
        Assert.Equal(ResearchQualityReviewNodeTypes.FinalizeCriticReport, validated.Actions[^1].NodeType);
    }

    [Fact]
    public void Validator_LocalOnly_RejectsInitialWebResearchCapability()
    {
        var request = new EquityLens.Api.Contracts.Research.ResearchAskRequest("2330", "7/17為什麼跌", SourcePolicy: EquityLens.Api.Contracts.Research.SourcePolicy.LocalOnly);
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid(), request);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.ResearchIntent] = new JsonObject(); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var actions = new DynamicPlanAction[] { new("web:initial", "retrieve-web-research-evidence", ResearchInvestigationNodeTypes.RetrieveWeb, [ResearchInvestigationNodeKeys.DetectIntent], new()) };
        var proposal = new DynamicPlanProposal(Guid.NewGuid(), run.OrchestrationVersion, DynamicPlanningTriggers.ResearchContextReady, DynamicGoalStatuses.Continue, "web", ["research-investigation"], actions);

        var error = Assert.Throws<InvalidOperationException>(() => new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal));

        Assert.Equal("Source policy LocalOnly forbids Web retrieval.", error.Message);
    }

    [Fact]
    public void DeterministicPlanner_WebOnly_OmitsLocalRetrievalCapability()
    {
        var request = new EquityLens.Api.Contracts.Research.ResearchAskRequest("2330", "最新消息", SourcePolicy: EquityLens.Api.Contracts.Research.SourcePolicy.WebOnly);
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid(), request);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.ResearchIntent] = new JsonObject(); run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, DynamicPlanningTriggers.ResearchContextReady, board, [ResearchInvestigationNodeTypes.Validate, ResearchInvestigationNodeTypes.DetectIntent], new WorkflowSkillCatalog().Skills, new NodeCapabilityRegistry().Capabilities, 0, 0);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);
        var validated = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);

        Assert.DoesNotContain(validated.Actions, x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveLocal);
        Assert.Contains(validated.Actions, x => x.NodeType == ResearchInvestigationNodeTypes.RetrieveWeb);
    }

    [Fact]
    public void DeterministicPlanner_EvidenceGap_ProducesBoundedIntentGraphWithoutRetrievalPlannerNode()
    {
        var context = Context(requiresRevision: true, requiresEvidence: true);
        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);

        Assert.Equal(DynamicGoalStatuses.Continue, proposal.GoalStatus);
        Assert.Contains(proposal.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x.Arguments["searchIntents"] is JsonArray);
        Assert.DoesNotContain(proposal.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.PlanRetrieval);
        Assert.Contains(proposal.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveWebEvidence);
        Assert.Equal(6, proposal.Actions.Count);
    }

    [Fact]
    public void DeterministicPlanner_CriticAfterDynamicInitialBranch_DependsOnMaterializedBranchEndpoint()
    {
        var context = Context(requiresRevision: true, requiresEvidence: true);
        context.Blackboard["dynamicLastNodeKey"] = "finalizeCriticReport:1";

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);

        Assert.Equal(["finalizeCriticReport:1"], proposal.Actions[0].DependsOn);
        Assert.Equal(EvidenceRemediationNodeTypes.ExtractClaims, proposal.Actions[0].NodeType);
    }

    [Fact]
    public async Task LlmPlanner_InvalidJsonTwice_UsesDeterministicFallback()
    {
        var chat = new InvalidChat(); var planner = new LlmAgentWorkflowPlanner(chat);
        var proposal = await planner.PlanAsync(Context(requiresRevision: false, requiresEvidence: false));

        Assert.Equal(2, chat.Calls);
        Assert.Equal("DeterministicFallback", proposal.Mode);
        Assert.Equal(DynamicGoalStatuses.Complete, proposal.GoalStatus);
        Assert.NotNull(proposal.FallbackReason);
    }

    [Fact]
    public async Task LlmPlanner_ReceivesMathToolManifestWithSchemaAndBlackboardContract()
    {
        var chat = new CapturingChat(); var planner = new LlmAgentWorkflowPlanner(chat);
        await planner.PlanAsync(Context(requiresRevision: false, requiresEvidence: false));

        Assert.Contains("\"tools\"", chat.Request!.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("calculate-sharpe-ratio", chat.Request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("parameters", chat.Request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("requiresBlackboard", chat.Request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains(AgentBlackboardKeys.MathInputs, chat.Request.UserPrompt, StringComparison.Ordinal);
        var prompt = JsonNode.Parse(chat.Request.UserPrompt)!.AsObject();
        var expectedShortfall = prompt["tools"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "calculate-expected-shortfall")!;
        Assert.Equal(["confidenceLevel"], expectedShortfall["parameters"]!["properties"]!.AsObject().Select(x => x.Key).ToArray());
        Assert.Null(expectedShortfall["parameters"]!["properties"]!["shrinkageAlpha"]);
    }

    [Theory]
    [InlineData("Wording", false, false)]
    [InlineData("EvidenceCorrection", true, false)]
    [InlineData("Calculation", true, true)]
    public void DeterministicPlanner_FeedbackRevision_ComposesIntentSpecificDag(string intent, bool expectsRetrieval, bool expectsMath)
    {
        var run = new FeedbackRevisionWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.FeedbackIntent] = intent;
        board[AgentBlackboardKeys.FeedbackComment] = intent == "Calculation" ? "請重算報酬率與風險數字。" : "請依照回饋修正答案內容。";
        board[AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(new EquityLens.Api.Contracts.Research.ResearchAskRequest("2454", "聯發科獲利如何？"), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.ResearchIntent] = new JsonObject { ["selected"] = 1 };
        board[AgentBlackboardKeys.SelectedEvidence] = new JsonArray();
        board[AgentBlackboardKeys.Ticker] = "2454";
        board[AgentBlackboardKeys.Question] = "聯發科獲利如何？";
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, DynamicPlanningTriggers.FeedbackContextReady, board,
            [FeedbackRevisionNodeTypes.LoadContext, FeedbackRevisionNodeTypes.ValidateContext], new WorkflowSkillCatalog().Skills,
            new NodeCapabilityRegistry().Capabilities, 0, 0, FeedbackRevisionNodeKeys.ValidateContext);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);
        var validated = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);

        Assert.Equal(expectsRetrieval, validated.Actions.Any(x => x.NodeType is ResearchInvestigationNodeTypes.RetrieveLocal or ResearchInvestigationNodeTypes.RetrieveWeb));
        Assert.Equal(expectsMath, validated.Actions.Any(x => x.NodeType.StartsWith("Calculate", StringComparison.Ordinal) || x.NodeType == PortfolioRiskMathNodeTypes.PrepareInputs));
        Assert.Contains(validated.Actions, x => x.NodeType == ResearchInvestigationNodeTypes.DraftAnswer);
        Assert.Contains(validated.Actions, x => x.NodeType == ResearchQualityReviewNodeTypes.CritiqueAnswer);
        Assert.Equal(ResearchQualityReviewNodeTypes.FinalizeCriticReport, validated.Actions[^1].NodeType);
    }

    [Fact]
    public async Task ValidatorAndMaterializer_ValidRevision_AppendsGraphArgumentsHistoryAndOutbox()
    {
        await using var db = CreateDb(); var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); board[AgentBlackboardKeys.Answer] = "answer"; board[AgentBlackboardKeys.CriticFindings] = new JsonArray(); board[AgentBlackboardKeys.CriticReview] = new JsonObject { [CriticReviewFields.RequiresRevision] = true, [CriticReviewFields.RequiresMoreEvidence] = false }; run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        db.AgentRuns.Add(run); await db.SaveChangesAsync();
        var actions = new DynamicPlanAction[] { new("draft:1", "revise-answer", DraftRevisionNodeTypes.DraftRevisedAnswer, [ResearchQualityReviewNodeKeys.FinalizeCriticReport], new JsonObject { ["reason"] = "critic" }), new("finalize:1", "finalize-revision", DraftRevisionNodeTypes.FinalizeRevision, ["draft:1"], new JsonObject()) };
        var proposal = new DynamicPlanProposal(Guid.NewGuid(), run.OrchestrationVersion, DynamicPlanningTriggers.CriticCompleted, DynamicGoalStatuses.Continue, "Revise", ["answer-revision"], actions);
        var registry = new NodeCapabilityRegistry(); var catalog = new AgentWorkflowCatalog(); var validated = new DynamicPlanValidator(registry, catalog, new WorkflowGraphTopologyService()).Validate(run, proposal);

        new GraphMaterializer(db, catalog).Materialize(run, validated); await db.SaveChangesAsync();

        Assert.Contains(run.Nodes, x => x.NodeKey == "draft:1" && x.InputJson!.Contains("critic", StringComparison.Ordinal));
        Assert.Contains("planningHistory", run.WorkflowDefinitionJson, StringComparison.Ordinal);
        Assert.Single(db.AgentRunWakeOutbox);
        Assert.Contains(db.AgentRunEvents, x => x.EventType == AgentEventTypes.GraphMaterialized);
    }

    [Fact]
    public void Validator_UnknownCapability_RejectsEntireProposal()
    {
        var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var proposal = new DynamicPlanProposal(Guid.NewGuid(), 0, DynamicPlanningTriggers.CriticCompleted, DynamicGoalStatuses.Continue, "bad", [], [new("bad", "invent-tool", "InventNode", [ResearchQualityReviewNodeKeys.FinalizeCriticReport], new JsonObject())]);
        var validator = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService());
        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(run, proposal));
        Assert.Contains("Unknown capability", error.Message, StringComparison.Ordinal);
        Assert.Equal(5, run.Nodes.Count);
    }

    [Fact]
    public void Validator_UsesCapabilitySpecificMathArgumentContract()
    {
        var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.MathInputs] = new JsonObject();
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        DynamicPlanProposal Proposal(JsonObject arguments) => new(Guid.NewGuid(), run.OrchestrationVersion,
            DynamicPlanningTriggers.BranchCompleted, DynamicGoalStatuses.Continue, "calculate ES",
            ["portfolio-risk-mathematics"],
            [new("expected-shortfall:1", "calculate-expected-shortfall", PortfolioRiskMathNodeTypes.Execute,
                [ResearchQualityReviewNodeKeys.FinalizeCriticReport], arguments)]);
        var validator = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService());

        var valid = validator.Validate(run, Proposal(new JsonObject { ["confidenceLevel"] = .975m }));
        var invalid = Assert.Throws<InvalidOperationException>(() => validator.Validate(run,
            Proposal(new JsonObject { ["confidenceLevel"] = .975m, ["shrinkageAlpha"] = .1m })));
        var injected = Assert.Throws<InvalidOperationException>(() => validator.Validate(run,
            Proposal(new JsonObject { ["returns"] = new JsonArray(.01m, -.02m) })));

        Assert.Single(valid.Actions);
        Assert.Contains("shrinkageAlpha", invalid.Message);
        Assert.Contains("Blackboard", injected.Message);
    }

    [Fact]
    public void Validator_ValidWebNodeBetweenExtractionAndAssessment_IsAccepted()
    {
        var run = RunWithEvidenceContext();
        var args = new JsonObject { ["searchIntents"] = new JsonArray(new JsonObject { ["topic"] = "TSMC capex", ["targetClaims"] = new JsonArray("claim-1"), ["preferredSourceRoles"] = new JsonArray("Primary"), ["freshness"] = "month", ["topK"] = 5 }) };
        DynamicPlanAction[] actions = [new("extract:1", "extract-claims", EvidenceRemediationNodeTypes.ExtractClaims, [ResearchQualityReviewNodeKeys.FinalizeCriticReport], new()), new("web:1", "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, ["extract:1"], args), new("assess:1", "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, ["web:1"], new())];
        var proposal = new DynamicPlanProposal(Guid.NewGuid(), run.OrchestrationVersion, DynamicPlanningTriggers.CriticCompleted, DynamicGoalStatuses.Continue, "web", ["evidence-remediation"], actions);
        var validated = new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal);
        Assert.Contains(validated.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveWebEvidence);
    }

    [Fact]
    public void Validator_SecondWebNode_IsRejected()
    {
        var run = RunWithEvidenceContext(); run.Nodes.Add(new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "web:old", NodeType = EvidenceRemediationNodeTypes.RetrieveWebEvidence, Status = AgentNodeStatuses.Succeeded });
        var args = new JsonObject { ["searchIntents"] = new JsonArray(new JsonObject { ["topic"] = "TSMC capex", ["targetClaims"] = new JsonArray("claim-1"), ["freshness"] = "month", ["topK"] = 5 }) };
        var proposal = new DynamicPlanProposal(Guid.NewGuid(), run.OrchestrationVersion, DynamicPlanningTriggers.EvidenceValidated, DynamicGoalStatuses.Continue, "web", ["evidence-remediation"], [new("web:2", "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, ["web:old"], args)]);
        var error = Assert.Throws<InvalidOperationException>(() => new DynamicPlanValidator(new NodeCapabilityRegistry(), new AgentWorkflowCatalog(), new WorkflowGraphTopologyService()).Validate(run, proposal));
        Assert.Equal("Web retrieval budget exceeded.", error.Message);
    }

    private static AgentRun RunWithEvidenceContext()
    {
        var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid()); var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.Question] = "TSMC capex?"; board[AgentBlackboardKeys.Answer] = "insufficient"; board[AgentBlackboardKeys.CriticFindings] = new JsonArray(); board[AgentBlackboardKeys.CriticReview] = new JsonObject { [CriticReviewFields.RequiresRevision] = true, [CriticReviewFields.RequiresMoreEvidence] = true }; run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); return run;
    }

    private static WorkflowPlanningContext Context(bool requiresRevision, bool requiresEvidence)
    {
        var board = new JsonObject { [AgentBlackboardKeys.Question] = "TSMC guidance?", [AgentBlackboardKeys.UnresolvedClaims] = new JsonArray("claim"), [AgentBlackboardKeys.CriticReview] = new JsonObject { [CriticReviewFields.RequiresRevision] = requiresRevision, [CriticReviewFields.RequiresMoreEvidence] = requiresEvidence } };
        return new(Guid.NewGuid(), 3, DynamicPlanningTriggers.CriticCompleted, board, [ResearchQualityReviewNodeTypes.FinalizeCriticReport], new WorkflowSkillCatalog().Skills, new NodeCapabilityRegistry().Capabilities, 0, 0);
    }
    private static EquityLensDbContext CreateDb() => new TestDb(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever(); modelBuilder.Entity<AgentRunWakeOutbox>().Property(x => x.Id).ValueGeneratedNever(); }
    }
    private sealed class InvalidChat : IChatCompletionService
    {
        public int Calls { get; private set; } public string Provider => "test"; public string Model => "invalid";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(new ChatCompletionResult("not-json", Model, 1, 1)); }
    }
    private sealed class CapturingChat : IChatCompletionService
    {
        public ChatCompletionRequest? Request { get; private set; } public string Provider => "test"; public string Model => "capture";
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) { Request = request; return Task.FromResult(new ChatCompletionResult("{\"goalStatus\":\"Complete\",\"reason\":\"done\",\"selectedSkills\":[],\"actions\":[]}", Model, 1, 1)); }
    }
}
