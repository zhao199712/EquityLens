namespace EquityLens.Api.Services.Agents;

public static class AgentWorkflowTypes
{
    public const string CriticReview = "CriticReview";
    public const string DraftRevision = "DraftRevision";
    public const string ResearchQualityReview = "ResearchQualityReview";
    public const string PortfolioDiagnosis = "PortfolioDiagnosis";
    public const string EvidenceRemediation = "EvidenceRemediation";
    public const string EvidenceReanalysis = "EvidenceReanalysis";
    public const string ResearchInvestigation = "ResearchInvestigation";
    public const string FeedbackRevision = "FeedbackRevision";
}

public static class AgentTypes
{
    public const string Critic = "CriticAgent";
    public const string Draft = "DraftAgent";
    public const string Portfolio = "PortfolioDiagnosisAgent";
    public const string Research = "ResearchAgent";
    public const string Analysis = "AnalysisAgent";
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
    public const int Version = 5;
    public const int LoopProfileVersion = 1;
    public const int MaxRetrievalIterations = 2;
    public const int MaxDynamicNodes = 18;
    public const int MaxWebRetrievals = 1;
}

public static class ResearchInvestigationWorkflow
{
    public const int Version = 1;
    public const int MaxInitialPlanNodes = 10;
}

public static class FeedbackRevisionWorkflow
{
    public const int Version = 1;
}

public static class FeedbackRevisionNodeKeys
{
    public const string LoadContext = "loadFeedbackRevisionContext";
    public const string ValidateContext = "validateFeedbackRevisionContext";
}

public static class FeedbackRevisionNodeTypes
{
    public const string LoadContext = "LoadFeedbackRevisionContext";
    public const string ValidateContext = "ValidateFeedbackRevisionContext";
}

public static class ResearchInvestigationNodeKeys
{
    public const string Validate = "validateResearchRequest";
    public const string DetectIntent = "detectResearchIntent";
    public const string PlanRetrieval = "planResearchRetrieval";
    public const string RetrieveLocal = "retrieveLocalResearchEvidence";
    public const string EvaluateEvidence = "evaluateInitialEvidencePolicy";
    public const string RetrieveWeb = "retrieveWebResearchEvidence";
    public const string RankEvidence = "rankAndSelectResearchEvidence";
    public const string DraftAnswer = "draftResearchAnswer";
}

public static class ResearchInvestigationNodeTypes
{
    public const string Validate = "ValidateResearchRequest";
    public const string DetectIntent = "DetectResearchIntent";
    public const string PlanRetrieval = "PlanResearchRetrieval";
    public const string RetrieveLocal = "RetrieveLocalResearchEvidence";
    public const string EvaluateEvidence = "EvaluateInitialEvidencePolicy";
    public const string RetrieveWeb = "RetrieveWebResearchEvidence";
    public const string RankEvidence = "RankAndSelectResearchEvidence";
    public const string DraftAnswer = "DraftResearchAnswer";
}

public static class PortfolioDiagnosisWorkflow
{
    public const int Version = 3;
    public const int LoopProfileVersion = 1;
    public const int MaxAnalysisIterations = 1;
    public const int MaxDynamicNodes = 10;
    public const int MaxMathCapabilitiesPerIteration = 4;
}

public static class EvidenceRemediationWorkflow
{
    public const int Version = 3;
    public const int MaxIterations = 2;
}

public static class EvidenceReanalysisWorkflow
{
    public const int Version = 1;
}

public static class EvidenceReanalysisNodeKeys
{
    public const string Load = "loadEvidenceRemediation";
    public const string Validate = "validateReanalysisRequest";
    public const string BuildContext = "buildAnalysisContext";
    public const string Reanalyze = "reanalyzeAnswer";
    public const string Critique = "critiqueReanalysis";
    public const string Revise = "reviseReanalysis";
    public const string Finalize = "finalizeReanalysis";
}

public static class EvidenceReanalysisNodeTypes
{
    public const string Load = "LoadEvidenceRemediation";
    public const string Validate = "ValidateReanalysisRequest";
    public const string BuildContext = "BuildAnalysisContext";
    public const string Reanalyze = "ReanalyzeAnswer";
    public const string Critique = "CritiqueReanalysis";
    public const string Revise = "ReviseReanalysis";
    public const string Finalize = "FinalizeReanalysis";
}

public static class EvidenceRemediationNodeKeys
{
    public const string LoadContext = "loadEvidenceRemediationContext";
    public const string PlanRetrieval = "planEvidenceRetrieval";
    public const string RetrieveEvidence = "retrieveRemediationEvidence";
    public const string RetrieveWebEvidence = "retrieveWebEvidence";
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
    public const string RetrieveWebEvidence = "RetrieveWebEvidence";
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
    public const string ResolveRiskEvidence = "resolvePortfolioRiskEvidence";
    public const string CalculateAttribution = "calculatePerformanceAttribution";
    public const string LoadRiskProfile = "loadRiskProfile";
    public const string PrioritizeRiskAnalyses = "prioritizeRiskAnalyses";
    public const string EvaluateQuality = "evaluatePortfolioDiagnosisQuality";
    public const string BuildEvidencePacket = "buildPortfolioEvidencePacket";
    public const string DraftDiagnosis = "draftPortfolioDiagnosis";
    public const string ApproveDiagnosis = "approvePortfolioDiagnosis";
    public const string FinalizeDiagnosis = "finalizePortfolioDiagnosis";
    public const string FinalizeRejectedDiagnosis = "finalizeRejectedPortfolioDiagnosis";
}

public static class PortfolioDiagnosisNodeTypes
{
    public const string LoadContext = "LoadPortfolioDiagnosisContext";
    public const string ResolveRiskEvidence = "ResolvePortfolioRiskEvidence";
    public const string CalculateAttribution = "CalculatePerformanceAttribution";
    public const string LoadRiskProfile = "LoadRiskProfile";
    public const string PrioritizeRiskAnalyses = "PrioritizeRiskAnalyses";
    public const string EvaluateQuality = "EvaluatePortfolioDiagnosisQuality";
    public const string BuildEvidencePacket = "BuildPortfolioEvidencePacket";
    public const string DraftDiagnosis = "DraftPortfolioDiagnosis";
    public const string FinalizeDiagnosis = "FinalizePortfolioDiagnosis";
    public const string FinalizeRejectedDiagnosis = "FinalizeRejectedPortfolioDiagnosis";
}

public static class PortfolioRiskMathNodeTypes
{
    public const string PrepareInputs = "PreparePortfolioRiskMathInputs";
    public const string Execute = "ExecutePortfolioRiskMath";
}

public static class PortfolioRiskMathNodeKeys
{
    public const string PrepareInputs = "preparePortfolioRiskMathInputs";
    public const string ExecuteCore = "executePortfolioRiskCoreMetrics";
}

public static class HumanApprovalNodeTypes
{
    public const string WaitForHumanApproval = "WaitForHumanApproval";
}

public static class HumanApprovalNodeKeys
{
    public const string WaitForHumanApproval = "waitForHumanApproval";
}

public static class HumanApprovalDecisions
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class HumanApprovalFields
{
    public const string ApprovalType = "approvalType";
    public const string Prompt = "prompt";
    public const string Subject = "subject";
    public const string NodeKey = "nodeKey";
    public const string RequestedAtUtc = "requestedAtUtc";
    public const string Decision = "decision";
    public const string Comment = "comment";
    public const string ReviewerId = "reviewerId";
    public const string DecidedAtUtc = "decidedAtUtc";
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
    public const string WaitingForApproval = "WaitingForApproval";
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
    public const string WaitingForApproval = "WaitingForApproval";
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
    public const string SupervisorPlanningStarted = "SupervisorPlanningStarted";
    public const string SchedulerDecision = "SchedulerDecision";
    public const string SupervisorRouteDecision = "SupervisorRouteDecision";
    public const string PlannerProposed = "PlannerProposed";
    public const string PlanValidated = "PlanValidated";
    public const string PlanRejected = "PlanRejected";
    public const string GraphMaterialized = "GraphMaterialized";
    public const string FeedbackSubmitted = "FeedbackSubmitted";
    public const string FeedbackContextLoaded = "FeedbackContextLoaded";
    public const string FollowUpRunCreated = "FollowUpRunCreated";
    public const string QuestionRouted = "QuestionRouted";
    public const string CapabilityRequested = "CapabilityRequested";
    public const string CapabilityRequestNotNeeded = "CapabilityRequestNotNeeded";
    public const string CapabilityRequestApproved = "CapabilityRequestApproved";
    public const string CapabilityRequestRejected = "CapabilityRequestRejected";
    public const string ApprovalRequested = "ApprovalRequested";
    public const string ApprovalDecision = "ApprovalDecision";
    public const string RunWaitingForFeedback = "RunWaitingForFeedback";
    public const string ApprovalApproved = "ApprovalApproved";
    public const string ApprovalRejected = "ApprovalRejected";
    public const string ApprovalCancelled = "ApprovalCancelled";
    public const string LoopStarted = "LoopStarted";
    public const string LoopIterationStarted = "LoopIterationStarted";
    public const string LoopDecisionMade = "LoopDecisionMade";
    public const string LoopIterationCompleted = "LoopIterationCompleted";
    public const string LoopStopped = "LoopStopped";
}

public static class AgentApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public static class AgentFeedbackTypes
{
    public const string Helpful = "Helpful";
    public const string NeedsCorrection = "NeedsCorrection";
}

public static class AgentFeedbackStatuses
{
    public const string Responded = "Responded";
}
