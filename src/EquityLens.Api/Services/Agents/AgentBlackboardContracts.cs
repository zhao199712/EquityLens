using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents;

public static class AgentBlackboardKeys
{
    public const string Ticker = "ticker";
    public const string Question = "question";
    public const string ResearchRunId = "researchRunId";
    public const string ResearchRun = "researchRun";
    public const string CriticReviewRunId = "criticReviewRunId";
    public const string CriticReviewRun = "criticReviewRun";
    public const string Answer = "answer";
    public const string Citations = "citations";
    public const string Steps = "steps";
    public const string Candidates = "candidates";
    public const string EvidenceChecks = "evidenceChecks";
    public const string CriticFindings = "criticFindings";
    public const string CriticReview = "criticReview";
    public const string SupervisorDecisions = "supervisorDecisions";
    public const string RevisedAnswer = "revisedAnswer";
    public const string RevisionSummary = "revisionSummary";
    public const string FinalOutput = "finalOutput";
}

public static class EvidenceCheckFields
{
    public const string CitationCount = "citationCount";
    public const string CandidateCount = "candidateCount";
    public const string SourceStatus = "sourceStatus";
    public const string FindingCount = "findingCount";
    public const string Findings = "findings";
}

public static class CriticReviewFields
{
    public const string Summary = "summary";
    public const string OverallSeverity = "overallSeverity";
    public const string Findings = "findings";
    public const string RequiresRevision = "requiresRevision";
    public const string RequiresMoreEvidence = "requiresMoreEvidence";
    public const string RouteBackTo = "routeBackTo";
    public const string RecommendedNextAction = "recommendedNextAction";
    public const string SuggestedAnswerRevision = "suggestedAnswerRevision";
}


public static class DraftRevisionFields
{
    public const string SourceAnswer = "sourceAnswer";
    public const string RevisedAnswer = "revisedAnswer";
    public const string RevisionSummary = "revisionSummary";
    public const string RevisionRequired = "revisionRequired";
    public const string AppliedRecommendation = "appliedRecommendation";
}

public static class CriticFindingFields
{
    public const string Severity = "severity";
    public const string Category = "category";
    public const string Message = "message";
    public const string RelatedCitationIndexes = "relatedCitationIndexes";
    public const string Recommendation = "recommendation";
}

public static class AgentBlackboardContracts
{
    public static JsonObject CreateInitialCriticReviewBlackboard(Guid researchRunId) => new()
    {
        [AgentBlackboardKeys.Ticker] = null,
        [AgentBlackboardKeys.Question] = null,
        [AgentBlackboardKeys.ResearchRunId] = researchRunId,
        [AgentBlackboardKeys.ResearchRun] = null,
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Steps] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.EvidenceChecks] = CreateEvidenceChecks(0, 0, string.Empty, new JsonArray()),
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.SupervisorDecisions] = new JsonArray(),
        [AgentBlackboardKeys.FinalOutput] = null
    };


    public static JsonObject CreateInitialDraftRevisionBlackboard(Guid criticReviewRunId) => new()
    {
        [AgentBlackboardKeys.CriticReviewRunId] = criticReviewRunId,
        [AgentBlackboardKeys.CriticReviewRun] = null,
        [AgentBlackboardKeys.Ticker] = null,
        [AgentBlackboardKeys.Question] = null,
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.RevisedAnswer] = null,
        [AgentBlackboardKeys.RevisionSummary] = null,
        [AgentBlackboardKeys.FinalOutput] = null
    };

    public static JsonObject CreateEvidenceChecks(
        int citationCount,
        int candidateCount,
        string sourceStatus,
        JsonArray findings) => new()
    {
        [EvidenceCheckFields.CitationCount] = citationCount,
        [EvidenceCheckFields.CandidateCount] = candidateCount,
        [EvidenceCheckFields.SourceStatus] = sourceStatus,
        [EvidenceCheckFields.FindingCount] = findings.Count,
        [EvidenceCheckFields.Findings] = findings.DeepClone()
    };

    public static JsonObject CreateFinalOutput(JsonObject criticReview)
    {
        var findings = criticReview[CriticReviewFields.Findings]?.AsArray() ?? [];
        return new JsonObject
        {
            [CriticReviewFields.Summary] = criticReview[CriticReviewFields.Summary]?.DeepClone(),
            [CriticReviewFields.OverallSeverity] = criticReview[CriticReviewFields.OverallSeverity]?.DeepClone(),
            [CriticReviewFields.Findings] = findings.DeepClone(),
            [CriticReviewFields.RequiresRevision] = criticReview[CriticReviewFields.RequiresRevision]?.DeepClone(),
            [CriticReviewFields.RequiresMoreEvidence] = criticReview[CriticReviewFields.RequiresMoreEvidence]?.DeepClone(),
            [CriticReviewFields.RouteBackTo] = criticReview[CriticReviewFields.RouteBackTo]?.DeepClone(),
            [CriticReviewFields.RecommendedNextAction] = criticReview[CriticReviewFields.RecommendedNextAction]?.DeepClone(),
            [CriticReviewFields.SuggestedAnswerRevision] = criticReview[CriticReviewFields.SuggestedAnswerRevision]?.DeepClone()
        };
    }

    public static JsonObject CreateFinding(string severity, string category, string message, string recommendation) => new()
    {
        [CriticFindingFields.Severity] = severity,
        [CriticFindingFields.Category] = category,
        [CriticFindingFields.Message] = message,
        [CriticFindingFields.RelatedCitationIndexes] = new JsonArray(),
        [CriticFindingFields.Recommendation] = recommendation
    };
}
