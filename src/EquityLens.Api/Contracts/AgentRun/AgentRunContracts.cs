using System.Text.Json;

namespace EquityLens.Api.Contracts.AgentRun;

// ── Requests ──

public sealed record CreateCriticReviewRequest(Guid ResearchRunId);

// ── Responses ──

public sealed record AgentRunCreatedResponse(Guid Id, string Status);

public sealed record AgentRunListItemResponse(
    Guid Id,
    string WorkflowType,
    string AgentType,
    string Status,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int NodeCount,
    int EventCount,
    int ToolCallCount);

public sealed record AgentRunDetailResponse(
    Guid Id,
    string WorkflowType,
    string AgentType,
    string Status,
    JsonElement InputJson,
    JsonElement? OutputJson,
    JsonElement BlackboardJson,
    JsonElement WorkflowDefinitionJson,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<AgentRunNodeDto> Nodes,
    IReadOnlyList<AgentRunEventDto> Events,
    IReadOnlyList<AgentToolCallDto> ToolCalls);

public sealed record AgentRunNodeDto(
    Guid Id,
    string NodeKey,
    string NodeType,
    string Status,
    JsonElement? InputJson,
    JsonElement? OutputJson,
    string? ErrorMessage,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMs);

public sealed record AgentRunEventDto(
    Guid Id,
    Guid? AgentRunNodeId,
    string EventType,
    string? Message,
    JsonElement? PayloadJson,
    DateTime CreatedAtUtc);

public sealed record AgentToolCallDto(
    Guid Id,
    Guid? AgentRunNodeId,
    string ToolName,
    string Status,
    JsonElement ArgumentsJson,
    string? ResultPreview,
    JsonElement? ResultJson,
    string? ErrorMessage,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMs);
