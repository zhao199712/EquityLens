namespace EquityLens.Api.Services.Agents;

public static class AgentWorkflowTypes
{
    public const string CriticReview = "CriticReview";
    public const string DraftRevision = "DraftRevision";
    public const string ResearchQualityReview = "ResearchQualityReview";
    public const string PortfolioDiagnosis = "PortfolioDiagnosis";
    public const string EvidenceRemediation = "EvidenceRemediation";
}

public static class AgentTypes
{
    public const string Critic = "CriticAgent";
    public const string Draft = "DraftAgent";
    public const string Portfolio = "PortfolioDiagnosisAgent";
    public const string Research = "ResearchAgent";
}

public static class CriticReviewWorkflow
{
    public const int Version = 1;
}

public static class DraftRevisionWorkflow
{
    public const int Version = 1;
}

public static class ResearchQualityReviewWorkflow
{
    public const int Version = 1;
}

public static class PortfolioDiagnosisWorkflow
{
    public const int Version = 1;
}

public static class EvidenceRemediationWorkflow
{
    public const int Version = 3;
    public const int MaxIterations = 2;
}

public static class EvidenceRemediationNodeKeys
{
    public const string LoadContext = "loadEvidenceRemediationContext";
    public const string PlanRetrieval = "planEvidenceRetrieval";
    public const string RetrieveEvidence = "retrieveRemediationEvidence";
    public const string ExtractClaims = "extractAnswerClaims";
    public const string AssessSupport = "assessClaimSupport";
    public const string ValidateMappings = "validateEvidenceMappings";
    public const string Route = "routeEvidenceRemediation";
    public const string BuildPacket = "buildRemediatedEvidencePacket";
    public const string DraftRevision = "draftEvidenceBackedRevision";
    public const string Finalize = "finalizeEvidenceRemediation";
}

public static class EvidenceRemediationNodeTypes
{
    public const string LoadContext = "LoadEvidenceRemediationContext";
    public const string PlanRetrieval = "PlanEvidenceRetrieval";
    public const string RetrieveEvidence = "RetrieveRemediationEvidence";
    public const string ExtractClaims = "ExtractAnswerClaims";
    public const string AssessSupport = "AssessClaimSupport";
    public const string ValidateMappings = "ValidateEvidenceMappings";
    public const string Route = "RouteEvidenceRemediation";
    public const string BuildPacket = "BuildRemediatedEvidencePacket";
    public const string DraftRevision = "DraftEvidenceBackedRevision";
    public const string Finalize = "FinalizeEvidenceRemediation";
}

public static class PortfolioDiagnosisNodeKeys
{
    public const string LoadContext = "loadPortfolioDiagnosisContext";
    public const string CalculateAttribution = "calculatePerformanceAttribution";
    public const string LoadRiskProfile = "loadRiskProfile";
    public const string PrioritizeRiskAnalyses = "prioritizeRiskAnalyses";
    public const string BuildEvidencePacket = "buildPortfolioEvidencePacket";
    public const string DraftDiagnosis = "draftPortfolioDiagnosis";
    public const string FinalizeDiagnosis = "finalizePortfolioDiagnosis";
}

public static class PortfolioDiagnosisNodeTypes
{
    public const string LoadContext = "LoadPortfolioDiagnosisContext";
    public const string CalculateAttribution = "CalculatePerformanceAttribution";
    public const string LoadRiskProfile = "LoadRiskProfile";
    public const string PrioritizeRiskAnalyses = "PrioritizeRiskAnalyses";
    public const string BuildEvidencePacket = "BuildPortfolioEvidencePacket";
    public const string DraftDiagnosis = "DraftPortfolioDiagnosis";
    public const string FinalizeDiagnosis = "FinalizePortfolioDiagnosis";
}

public static class CriticReviewNodeKeys
{
    public const string LoadResearchRun = "loadResearchRun";
    public const string BuildEvidencePacket = "buildEvidencePacket";
    public const string CheckEvidence = "checkEvidence";
    public const string CritiqueAnswer = "critiqueAnswer";
    public const string FinalizeCriticReport = "finalizeCriticReport";
}

public static class CriticReviewNodeTypes
{
    public const string LoadResearchRun = "LoadResearchRun";
    public const string BuildEvidencePacket = "BuildEvidencePacket";
    public const string CheckEvidence = "CheckEvidence";
    public const string CritiqueAnswer = "CritiqueAnswer";
    public const string FinalizeCriticReport = "FinalizeCriticReport";
}

public static class DraftRevisionNodeKeys
{
    public const string LoadCriticReviewRun = "loadCriticReviewRun";
    public const string DraftRevisedAnswer = "draftRevisedAnswer";
    public const string FinalizeRevision = "finalizeRevision";
}

public static class DraftRevisionNodeTypes
{
    public const string LoadCriticReviewRun = "LoadCriticReviewRun";
    public const string DraftRevisedAnswer = "DraftRevisedAnswer";
    public const string FinalizeRevision = "FinalizeRevision";
}

public static class AgentRunStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string WaitingForFeedback = "WaitingForFeedback";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class AgentNodeStatuses
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string WaitingForFeedback = "WaitingForFeedback";
    public const string Cancelled = "Cancelled";
}

public static class AgentToolCallStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class ResearchQualityReviewNodeKeys
{
    public const string LoadResearchRun = "loadResearchRun";
    public const string BuildEvidencePacket = "buildEvidencePacket";
    public const string CheckEvidence = "checkEvidence";
    public const string CritiqueAnswer = "critiqueAnswer";
    public const string FinalizeCriticReport = "finalizeCriticReport";
    public const string DraftRevisedAnswer = "draftRevisedAnswer";
    public const string FinalizeRevision = "finalizeRevision";
}

public static class ResearchQualityReviewNodeTypes
{
    public const string LoadResearchRun = "LoadResearchRun";
    public const string BuildEvidencePacket = "BuildEvidencePacket";
    public const string CheckEvidence = "CheckEvidence";
    public const string CritiqueAnswer = "CritiqueAnswer";
    public const string FinalizeCriticReport = "FinalizeCriticReport";
    public const string DraftRevisedAnswer = "DraftRevisedAnswer";
    public const string FinalizeRevision = "FinalizeRevision";
}

public static class AgentEventTypes
{
    public const string RunCreated = "RunCreated";
    public const string RunStarted = "RunStarted";
    public const string RunSucceeded = "RunSucceeded";
    public const string RunFailed = "RunFailed";
    public const string RunCancelled = "RunCancelled";
    public const string NodeReady = "NodeReady";
    public const string NodeStarted = "NodeStarted";
    public const string NodeCompleted = "NodeCompleted";
    public const string NodeFailed = "NodeFailed";
    public const string ToolCallStarted = "ToolCallStarted";
    public const string ToolCallCompleted = "ToolCallCompleted";
    public const string ToolCallFailed = "ToolCallFailed";
    public const string BlackboardUpdated = "BlackboardUpdated";
    public const string SupervisorDecision = "SupervisorDecision";
}
