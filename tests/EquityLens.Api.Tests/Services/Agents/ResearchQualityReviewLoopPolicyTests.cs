using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchQualityReviewLoopPolicyTests
{
    [Fact]
    public void Evaluate_AcceptedCritic_StopsWithQualityGatePassed()
    {
        var run = CreateRun(ResearchQualityReviewNodeTypes.FinalizeCriticReport);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.CriticReview] = new JsonObject
        {
            [CriticReviewFields.RequiresMoreEvidence] = false,
            [CriticReviewFields.RequiresRevision] = false
        };
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var decision = new ResearchQualityReviewLoopPolicy().Evaluate(run);

        Assert.True(decision.IsTerminal);
        Assert.Equal(AgentLoopActions.Complete, decision.Action);
        Assert.Equal(AgentLoopStopReasons.QualityGatePassed, decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_EvidenceGap_StartsFirstBoundedRetrievalIteration()
    {
        var run = CreateRun(ResearchQualityReviewNodeTypes.FinalizeCriticReport);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.CriticReview] = new JsonObject
        {
            [CriticReviewFields.RequiresMoreEvidence] = true,
            [CriticReviewFields.RequiresRevision] = true
        };
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var decision = new ResearchQualityReviewLoopPolicy().Evaluate(run);

        Assert.False(decision.IsTerminal);
        Assert.Equal(AgentLoopActions.RetrieveEvidence, decision.Action);
        Assert.Equal(1, decision.Iteration);
        var updated = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        Assert.Equal("Running", updated[AgentBlackboardKeys.Runtime]?["status"]?.GetValue<string>());
    }

    [Fact]
    public void Evaluate_UnchangedGapAndEvidence_FinalizesForNoProgress()
    {
        var run = CreateRun(EvidenceRemediationNodeTypes.Route, iteration: 1);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.RouteDecision] = "InsufficientEvidence";
        board[AgentBlackboardKeys.UnresolvedClaims] = new JsonArray(new JsonObject { ["claimId"] = "C1" });
        board[AgentBlackboardKeys.Runtime]!["iteration"] = 1;
        board[AgentBlackboardKeys.Runtime]!["evidenceBaseline"] = 0;
        board[AgentBlackboardKeys.Runtime]!["unresolvedClaimsBaseline"] = "C1";
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var decision = new ResearchQualityReviewLoopPolicy().Evaluate(run);

        Assert.Equal(AgentLoopActions.FinalizeLimited, decision.Action);
        Assert.Equal(AgentLoopStopReasons.NoProgress, decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_SecondIterationWithProgress_FinalizesAtIterationBudget()
    {
        var run = CreateRun(EvidenceRemediationNodeTypes.Route, iteration: 2);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.RouteDecision] = "PartiallySupportedNeedsRetrieval";
        board[AgentBlackboardKeys.RetrievedEvidence] = new JsonArray(new JsonObject { ["url"] = "https://example.test/evidence" });
        board[AgentBlackboardKeys.Runtime]!["iteration"] = 2;
        board[AgentBlackboardKeys.Runtime]!["evidenceBaseline"] = 0;
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var decision = new ResearchQualityReviewLoopPolicy().Evaluate(run);

        Assert.Equal(AgentLoopActions.FinalizeLimited, decision.Action);
        Assert.Equal(AgentLoopStopReasons.MaxIterations, decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_UnresolvedClaimIdsStoredAsStrings_AreAccepted()
    {
        var run = CreateRun(EvidenceRemediationNodeTypes.Route, iteration: 1);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.RouteDecision] = "InsufficientEvidence";
        board[AgentBlackboardKeys.UnresolvedClaims] = new JsonArray("claim-1", "claim-2");
        board[AgentBlackboardKeys.Runtime]!["iteration"] = 1;
        board[AgentBlackboardKeys.Runtime]!["unresolvedClaimsBaseline"] = "claim-1|claim-2";
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);

        var decision = new ResearchQualityReviewLoopPolicy().Evaluate(run);

        Assert.Equal(AgentLoopActions.FinalizeLimited, decision.Action);
        Assert.Equal(AgentLoopStopReasons.NoProgress, decision.ReasonCode);
        Assert.Equal(["claim-1", "claim-2"], decision.UnresolvedClaimIds);
    }

    private static AgentRun CreateRun(string lastNodeType, int iteration = 0)
    {
        var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        run.Nodes =
        [
            new AgentRunNode
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "last", NodeType = lastNodeType,
                Status = AgentNodeStatuses.Succeeded, Iteration = iteration, CompletedAtUtc = DateTime.UtcNow
            }
        ];
        return run;
    }
}
