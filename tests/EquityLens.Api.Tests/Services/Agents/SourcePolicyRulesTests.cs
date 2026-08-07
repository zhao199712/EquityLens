using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class SourcePolicyRulesTests
{
    [Theory]
    [InlineData(SourcePolicy.LocalOnly, 1, false, true, true, false)]
    [InlineData(SourcePolicy.WebOnly, 1, false, false, false, true)]
    [InlineData(SourcePolicy.LocalThenWeb, 1, false, true, true, false)]
    [InlineData(SourcePolicy.LocalThenWeb, 2, false, false, false, true)]
    [InlineData(SourcePolicy.LocalAndWeb, 1, false, false, true, true)]
    [InlineData(SourcePolicy.LocalAndWeb, 2, true, false, true, false)]
    [InlineData(SourcePolicy.Auto, 1, false, true, true, true)]
    [InlineData(SourcePolicy.Auto, 1, false, false, true, false)]
    [InlineData(SourcePolicy.Auto, 2, false, false, false, true)]
    [InlineData(SourcePolicy.Auto, 2, true, false, true, false)]
    public void SelectRemediationSources_ImplementsPolicyMatrix(
        SourcePolicy policy,
        int iteration,
        bool webAlreadyUsed,
        bool freshnessSensitive,
        bool expectedLocal,
        bool expectedWeb)
    {
        var selection = SourcePolicyRules.SelectRemediationSources(policy, iteration, webAlreadyUsed, freshnessSensitive);

        Assert.Equal(expectedLocal, selection.UseLocal);
        Assert.Equal(expectedWeb, selection.UseWeb);
    }

    [Theory]
    [InlineData(SourcePolicy.LocalOnly, 1, false, true, false)]
    [InlineData(SourcePolicy.WebOnly, 1, false, false, true)]
    [InlineData(SourcePolicy.LocalThenWeb, 1, false, true, false)]
    [InlineData(SourcePolicy.LocalThenWeb, 2, false, false, true)]
    [InlineData(SourcePolicy.LocalAndWeb, 1, false, true, true)]
    [InlineData(SourcePolicy.Auto, 1, true, true, true)]
    [InlineData(SourcePolicy.Auto, 2, false, false, true)]
    public void LoopPlanner_ProducesOnlyPolicyAuthorizedRetrievalNodes(
        SourcePolicy policy,
        int iteration,
        bool freshnessSensitive,
        bool expectedLocal,
        bool expectedWeb)
    {
        var question = freshnessSensitive ? "台積電今天最新展望為何？" : "台積電競爭優勢為何？";
        var board = AgentBlackboardContracts.CreateInitialResearchQualityReviewBlackboard(Guid.NewGuid());
        board[AgentBlackboardKeys.Question] = question;
        board[AgentBlackboardKeys.Answer] = "answer";
        board[AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(
            new ResearchAskRequest("2330", question, SourcePolicy: policy), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.CriticReview] = new JsonObject
        {
            [CriticReviewFields.RequiresRevision] = true,
            [CriticReviewFields.RequiresMoreEvidence] = true
        };
        var decision = new AgentLoopDecision(
            AgentLoopActions.RetrieveEvidence, "EVIDENCE_GAP", "補充證據。", iteration, false, 0, []);
        var context = new WorkflowPlanningContext(
            Guid.NewGuid(), 1, DynamicPlanningTriggers.CriticCompleted, board,
            [ResearchQualityReviewNodeTypes.FinalizeCriticReport],
            new WorkflowSkillCatalog().Skills, new NodeCapabilityRegistry().Capabilities,
            iteration - 1, 0, ResearchQualityReviewNodeKeys.FinalizeCriticReport, decision);

        var proposal = DeterministicDynamicWorkflowPlanner.Create(context);

        Assert.Equal(expectedLocal, proposal.Actions.Any(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence));
        Assert.Equal(expectedWeb, proposal.Actions.Any(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveWebEvidence));
        var local = proposal.Actions.SingleOrDefault(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence);
        if (local is not null) Assert.False(local.Arguments["allowWebFallback"]?.GetValue<bool>() ?? true);
    }
}
