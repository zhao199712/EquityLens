namespace EquityLens.Api.Contracts.Agents;

public sealed record CreateCriticReviewRequest(Guid ResearchRunId);

public sealed record CreateDraftRevisionRequest(Guid CriticReviewRunId);

public sealed record CreateResearchQualityReviewRequest(Guid ResearchRunId);
public sealed record CreateEvidenceRemediationRequest(Guid CriticReviewRunId);

public sealed record CreatePortfolioDiagnosisRequest(DateOnly? From = null, DateOnly? To = null);

public sealed record AgentRunSummaryResponse(
    Guid Id,
    string WorkflowType,
    string AgentType,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

public sealed record AgentRunNodeResponse(
    Guid Id,
    string NodeKey,
    string NodeType,
    string Status,
    string? InputJson,
    string? OutputJson,
    string? ErrorMessage,
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
    DateTime? RespondedAtUtc);

public sealed record AgentRunDetailResponse(
    AgentRunSummaryResponse Run,
    IReadOnlyList<AgentRunNodeResponse> Nodes,
    IReadOnlyList<AgentRunEventResponse> Events,
    IReadOnlyList<AgentToolCallResponse> ToolCalls,
    IReadOnlyList<AgentFeedbackResponse> Feedback,
    string BlackboardJson,
    string? OutputJson,
    string WorkflowDefinitionJson);
