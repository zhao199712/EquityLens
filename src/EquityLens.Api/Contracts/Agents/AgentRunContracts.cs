namespace EquityLens.Api.Contracts.Agents;

public sealed record CreateCriticReviewRequest(Guid ResearchRunId);

public sealed record CreateDraftRevisionRequest(Guid CriticReviewRunId);

public sealed record CreateResearchQualityReviewRequest(Guid ResearchRunId);
public sealed record CreateEvidenceRemediationRequest(Guid CriticReviewRunId);
public sealed record CreateEvidenceReanalysisRequest(Guid EvidenceRemediationRunId);

public sealed record CreatePortfolioDiagnosisRequest(DateOnly? From = null, DateOnly? To = null);

public sealed record SubmitAgentFeedbackRequest(Guid RequestId, string FeedbackType, string? Comment = null);

public sealed record AgentRunSummaryResponse(
    Guid Id,
    string WorkflowType,
    string AgentType,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalEstimatedCostUsd,
    Guid? ParentAgentRunId = null);

public sealed record AgentRunNodeResponse(
    Guid Id,
    string NodeKey,
    string TemplateNodeKey,
    int Iteration,
    string NodeType,
    string DisplayName,
    string? Description,
    string Stage,
    string Status,
    string? InputJson,
    string? OutputJson,
    string? ErrorMessage,
    string? ErrorCode,
    string? ErrorCategory,
    bool? ErrorRetryable,
    int? InputBlackboardVersion,
    int? OutputBlackboardVersion,
    string[]? ProducedBlackboardKeys,
    string? BlackboardSnapshotJson,
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMs);

public sealed record AgentRunEventResponse(
    Guid Id,
    Guid? AgentRunNodeId,
    string EventType,
    string? Message,
    string? PayloadJson,
    DateTime CreatedAtUtc);

public sealed record AgentToolCallResponse(
    Guid Id,
    Guid? AgentRunNodeId,
    string ToolName,
    string Status,
    string ArgumentsJson,
    string? ResultPreview,
    string? ResultJson,
    string? ErrorMessage,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMs);

public sealed record AgentFeedbackResponse(
    Guid Id,
    Guid? AgentRunNodeId,
    string FeedbackType,
    string Status,
    string Prompt,
    string? ResponseJson,
    DateTime CreatedAtUtc,
    DateTime? RespondedAtUtc,
    Guid ClientRequestId = default,
    Guid? FollowUpAgentRunId = null);

public sealed record SubmitAgentFeedbackResponse(
    AgentFeedbackResponse Feedback,
    AgentRunSummaryResponse? FollowUpAgentRun,
    Guid? FollowUpResearchRunId);

public sealed record AgentRunDetailResponse(
    AgentRunSummaryResponse Run,
    IReadOnlyList<AgentRunNodeResponse> Nodes,
    IReadOnlyList<AgentRunEventResponse> Events,
    IReadOnlyList<AgentToolCallResponse> ToolCalls,
    IReadOnlyList<AgentFeedbackResponse> Feedback,
    string BlackboardJson,
    string? OutputJson,
    string WorkflowDefinitionJson);
