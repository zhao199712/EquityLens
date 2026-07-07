using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents;

public sealed record WorkflowPolicyContext(
    string WorkflowType,
    string CurrentNodeKey,
    JsonObject Blackboard,
    JsonObject? NodeOutput);

public sealed record WorkflowPolicyDecision(
    bool ShouldContinue,
    bool ShouldStop,
    bool RequiresRevision,
    bool RequiresMoreEvidence,
    string? RouteBackTo,
    string RecommendedNextAction,
    string Reason);

public interface IWorkflowPolicyEvaluator
{
    string WorkflowType { get; }

    WorkflowPolicyDecision Evaluate(WorkflowPolicyContext context);
}

public sealed class CriticReviewPolicyEvaluator : IWorkflowPolicyEvaluator
{
    private static readonly string[] EvidenceIssueCategories =
    [
        "MissingCitation",
        "InsufficientEvidence",
        "WeakCitation"
    ];

    public string WorkflowType => AgentWorkflowTypes.CriticReview;

    public WorkflowPolicyDecision Evaluate(WorkflowPolicyContext context)
    {
        if (!string.Equals(context.WorkflowType, WorkflowType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported workflow type '{context.WorkflowType}'.");
        }

        var criticReview = context.NodeOutput
            ?? AgentNodeJson.GetRequiredBlackboardObject(context.Blackboard, AgentBlackboardKeys.CriticReview);
        var findings = (criticReview[CriticReviewFields.Findings]?.AsArray() ?? [])
            .Select(AgentNodeJson.ParseFinding)
            .Where(x => x is not null)
            .Cast<CriticFinding>()
            .ToList();

        if (findings.Count == 0)
        {
            return new WorkflowPolicyDecision(
                ShouldContinue: false,
                ShouldStop: true,
                RequiresRevision: false,
                RequiresMoreEvidence: false,
                RouteBackTo: null,
                RecommendedNextAction: "AcceptAnswer",
                Reason: "CriticReview did not report findings.");
        }

        var requiresMoreEvidence = findings.Any(f => EvidenceIssueCategories.Contains(f.Category, StringComparer.OrdinalIgnoreCase));
        if (requiresMoreEvidence)
        {
            return new WorkflowPolicyDecision(
                ShouldContinue: false,
                ShouldStop: true,
                RequiresRevision: true,
                RequiresMoreEvidence: true,
                RouteBackTo: "ResearchRetrieval",
                RecommendedNextAction: "CollectMoreEvidenceThenReviseAnswer",
                Reason: "CriticReview found citation or evidence coverage issues.");
        }

        return new WorkflowPolicyDecision(
            ShouldContinue: false,
            ShouldStop: true,
            RequiresRevision: true,
            RequiresMoreEvidence: false,
            RouteBackTo: "AnswerGeneration",
            RecommendedNextAction: "ReviseAnswer",
            Reason: "CriticReview found answer quality issues without evidence retrieval gaps.");
    }
}
