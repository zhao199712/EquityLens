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
    public const string EvidencePacket = "evidencePacket";
    public const string EvidenceChecks = "evidenceChecks";
    public const string CriticFindings = "criticFindings";
    public const string CriticReview = "criticReview";
    public const string SupervisorDecisions = "supervisorDecisions";
    public const string RevisedAnswer = "revisedAnswer";
    public const string RevisionSummary = "revisionSummary";
    public const string AppliedRecommendation = "appliedRecommendation";
    public const string FinalOutput = "finalOutput";
    public const string PortfolioId = "portfolioId";
    public const string DiagnosisFrom = "diagnosisFrom";
    public const string DiagnosisTo = "diagnosisTo";
    public const string PortfolioContext = "portfolioContext";
    public const string PerformanceAttribution = "performanceAttribution";
    public const string RiskProfile = "riskProfile";
    public const string RiskAnalysisPriorities = "riskAnalysisPriorities";
    public const string PortfolioEvidencePacket = "portfolioEvidencePacket";
    public const string PortfolioDiagnosisDraft = "portfolioDiagnosisDraft";
    public const string RetrievalPlan = "retrievalPlan";
    public const string RetrievedEvidence = "retrievedEvidence";
    public const string ExtractedClaims = "extractedClaims";
    public const string ClaimSupportAssessments = "claimSupportAssessments";
    public const string EvidenceValidationResults = "evidenceValidationResults";
    public const string RemediatedEvidencePacket = "remediatedEvidencePacket";
    public const string RetrievalHistory = "retrievalHistory";
    public const string RouteDecision = "routeDecision";
    public const string UnresolvedClaims = "unresolvedClaims";
    public const string Runtime = "runtime";
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
        [AgentBlackboardKeys.EvidencePacket] = null,
        [AgentBlackboardKeys.EvidenceChecks] = CreateEvidenceChecks(0, 0, string.Empty, new JsonArray()),
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.SupervisorDecisions] = new JsonArray(),
        [AgentBlackboardKeys.FinalOutput] = null
    };

    public static JsonObject CreateInitialPortfolioDiagnosisBlackboard(Guid portfolioId, DateOnly from, DateOnly to) => new()
    {
        [AgentBlackboardKeys.PortfolioId] = portfolioId,
        [AgentBlackboardKeys.DiagnosisFrom] = from.ToString("yyyy-MM-dd"),
        [AgentBlackboardKeys.DiagnosisTo] = to.ToString("yyyy-MM-dd"),
        [AgentBlackboardKeys.PortfolioContext] = null,
        [AgentBlackboardKeys.PerformanceAttribution] = null,
        [AgentBlackboardKeys.RiskProfile] = null,
        [AgentBlackboardKeys.RiskAnalysisPriorities] = new JsonArray(),
        [AgentBlackboardKeys.PortfolioEvidencePacket] = null,
        [AgentBlackboardKeys.PortfolioDiagnosisDraft] = null,
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
        [AgentBlackboardKeys.AppliedRecommendation] = null,
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

    public static JsonObject CreateFinalOutput(JsonObject criticReview, WorkflowPolicyDecision policyDecision)
    {
        var findings = criticReview[CriticReviewFields.Findings]?.AsArray() ?? [];
        return new JsonObject
        {
            [CriticReviewFields.Summary] = criticReview[CriticReviewFields.Summary]?.DeepClone(),
            [CriticReviewFields.OverallSeverity] = criticReview[CriticReviewFields.OverallSeverity]?.DeepClone(),
            [CriticReviewFields.Findings] = findings.DeepClone(),
            [CriticReviewFields.RequiresRevision] = policyDecision.RequiresRevision,
            [CriticReviewFields.RequiresMoreEvidence] = policyDecision.RequiresMoreEvidence,
            [CriticReviewFields.RouteBackTo] = policyDecision.RouteBackTo,
            [CriticReviewFields.RecommendedNextAction] = policyDecision.RecommendedNextAction,
            [CriticReviewFields.SuggestedAnswerRevision] = criticReview[CriticReviewFields.SuggestedAnswerRevision]?.DeepClone()
        };
    }

    public static JsonObject CreateInitialResearchQualityReviewBlackboard(Guid researchRunId) => new()
    {
        [AgentBlackboardKeys.Ticker] = null,
        [AgentBlackboardKeys.Question] = null,
        [AgentBlackboardKeys.ResearchRunId] = researchRunId,
        [AgentBlackboardKeys.ResearchRun] = null,
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Steps] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.EvidencePacket] = null,
        [AgentBlackboardKeys.EvidenceChecks] = CreateEvidenceChecks(0, 0, string.Empty, new JsonArray()),
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.SupervisorDecisions] = new JsonArray(),
        [AgentBlackboardKeys.RevisedAnswer] = null,
        [AgentBlackboardKeys.RevisionSummary] = null,
        [AgentBlackboardKeys.AppliedRecommendation] = null,
        [AgentBlackboardKeys.FinalOutput] = null
    };

    public static JsonObject CreateInitialEvidenceRemediationBlackboard(Guid criticReviewRunId) => new()
    {
        [AgentBlackboardKeys.CriticReviewRunId] = criticReviewRunId,
        [AgentBlackboardKeys.CriticReviewRun] = null,
        [AgentBlackboardKeys.ResearchRunId] = null,
        [AgentBlackboardKeys.Ticker] = null,
        [AgentBlackboardKeys.Question] = null,
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.RetrievalPlan] = null,
        [AgentBlackboardKeys.RetrievedEvidence] = new JsonArray(),
        [AgentBlackboardKeys.ExtractedClaims] = new JsonArray(),
        [AgentBlackboardKeys.ClaimSupportAssessments] = new JsonArray(),
        [AgentBlackboardKeys.EvidenceValidationResults] = null,
        [AgentBlackboardKeys.RemediatedEvidencePacket] = null,
        [AgentBlackboardKeys.RetrievalHistory] = new JsonArray(),
        [AgentBlackboardKeys.RouteDecision] = null,
        [AgentBlackboardKeys.UnresolvedClaims] = new JsonArray(),
        [AgentBlackboardKeys.Runtime] = new JsonObject { ["iteration"] = 0, ["maxIterations"] = EvidenceRemediationWorkflow.MaxIterations, ["webFallbackCount"] = 0 },
        [AgentBlackboardKeys.RevisedAnswer] = null,
        [AgentBlackboardKeys.RevisionSummary] = null,
        [AgentBlackboardKeys.FinalOutput] = null
    };

    public static JsonObject CreateFinding(string severity, string category, string message, string recommendation) => new()
    {
        [CriticFindingFields.Severity] = severity,
        [CriticFindingFields.Category] = category,
        [CriticFindingFields.Message] = message,
        [CriticFindingFields.RelatedCitationIndexes] = new JsonArray(),
        [CriticFindingFields.Recommendation] = recommendation
    };
}
