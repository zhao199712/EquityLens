namespace EquityLens.Api.Services.Agents;

public static class AgentWorkflowTypes
{
    public const string CriticReview = "CriticReview";
    public const string DraftRevision = "DraftRevision";
}

public static class AgentTypes
{
    public const string Critic = "CriticAgent";
    public const string Draft = "DraftAgent";
}

public static class CriticReviewWorkflow
{
    public const int Version = 1;
}

public static class DraftRevisionWorkflow
{
    public const int Version = 1;
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
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string WaitingForFeedback = "WaitingForFeedback";
}

public static class AgentToolCallStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
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
