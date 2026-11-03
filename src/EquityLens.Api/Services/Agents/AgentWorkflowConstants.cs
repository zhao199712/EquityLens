namespace EquityLens.Api.Services.Agents;

public static class AgentWorkflowTypes
{
    public const string ResearchAgent = "ResearchAgent";
    public static bool OwnsResearchArtifact(string type) => type == ResearchAgent || LegacyWorkflowReadModel.OwnsResearchArtifact(type);
    public const string CriticReview = "CriticReview";
    public const string PortfolioDiagnosis = "PortfolioDiagnosis";
}

public static class AgentTypes
{
    public const string Critic = "CriticAgent";
    public const string Portfolio = "PortfolioDiagnosisAgent";
    public const string Research = "ResearchAgent";
    public const string Analysis = "AnalysisAgent";
}

public static class CriticReviewWorkflow
{
    public const int Version = 2;
}

public static class ResearchEntryNodeTypes
{
    public const string Validate = "ValidateResearchRequest";
    public const string DetectIntent = "DetectResearchIntent";

}

public static class PortfolioDiagnosisWorkflow
{
    public const int Version = 4;
}

public static class PortfolioDiagnosisNodeKeys
{
    public const string LoadContext = "loadPortfolioDiagnosisContext";
    public const string ApproveDiagnosis = "approvePortfolioDiagnosis";
    public const string FinalizeDiagnosis = "finalizePortfolioDiagnosis";
    public const string FinalizeRejectedDiagnosis = "finalizeRejectedPortfolioDiagnosis";
}

public static class PortfolioDiagnosisNodeTypes
{
    public const string LoadContext = "LoadPortfolioDiagnosisContext";
    public const string FinalizeDiagnosis = "FinalizePortfolio";
    public const string FinalizeRejectedDiagnosis = "FinalizeRejectedPortfolio";
}

public static class HumanApprovalNodeTypes
{
    public const string WaitForHumanApproval = "WaitForHumanApproval";
}

public static class HumanApprovalDecisions
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class HumanApprovalFields
{
    public const string Decision = "decision";
    public const string Comment = "comment";
    public const string ReviewerId = "reviewerId";
    public const string DecidedAtUtc = "decidedAtUtc";
}

public static class CriticReviewNodeKeys
{
    public const string LoadResearchRun = "loadResearchRun";
    public const string BuildEvidencePacket = "buildEvidencePacket";
    public const string FinalizeCriticReport = "finalizeCriticReport";
}

public static class CriticReviewNodeTypes
{
    public const string LoadResearchRun = "LoadResearchRun";
    public const string FinalizeCriticReport = "FinalizeCriticReport";
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
    public const string SchedulerDecision = "SchedulerDecision";
    public const string SupervisorRouteDecision = "SupervisorRouteDecision";
    public const string FeedbackSubmitted = "FeedbackSubmitted";
    public const string FollowUpRunCreated = "FollowUpRunCreated";
    public const string QuestionRouted = "QuestionRouted";
    public const string ApprovalRequested = "ApprovalRequested";
    public const string ApprovalDecision = "ApprovalDecision";
    public const string RunWaitingForFeedback = "RunWaitingForFeedback";
    public const string ApprovalApproved = "ApprovalApproved";
    public const string ApprovalRejected = "ApprovalRejected";
    public const string ApprovalCancelled = "ApprovalCancelled";

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
