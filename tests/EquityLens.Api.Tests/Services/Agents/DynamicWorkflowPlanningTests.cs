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
    public void DeterministicPlanner_EvidenceGap_ProducesBoundedIntentGraphWithoutRetrievalPlannerNode()
    {
        var context = Context(requiresRevision: true, requiresEvidence: true);
        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);

        Assert.Equal(DynamicGoalStatuses.Continue, proposal.GoalStatus);
        Assert.Contains(proposal.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x.Arguments["searchIntents"] is JsonArray);
        Assert.DoesNotContain(proposal.Actions, x => x.NodeType == EvidenceRemediationNodeTypes.PlanRetrieval);
        Assert.Equal(5, proposal.Actions.Count);
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
}
