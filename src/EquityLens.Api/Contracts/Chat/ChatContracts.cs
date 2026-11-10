namespace EquityLens.Api.Contracts.Chat;

public sealed record CreateSessionRequest(string? Title = null);

public sealed record CreateSessionResponse(
    Guid Id,
    string? Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int MessageCount);

public sealed record SendMessageRequest(
    string Content,
    Guid RequestId);

public sealed record ChatSessionDto(
    Guid Id,
    string? Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int MessageCount);

public sealed record ChatMessageDto(
    Guid Id,
    string Role,
    string? Content,
    string? ToolName,
    int? SequenceNumber,
    DateTime CreatedAtUtc,
    string MessageType = "Text",
    Guid? AgentRunId = null,
    ConversationRunCardDto? RunCard = null,
    IReadOnlyList<string>? Choices = null);

public sealed record ConversationRunCardDto(
    Guid AgentRunId,
    Guid? ResearchRunId,
    string WorkflowType,
    string Status,
    string? CurrentStage,
    string? CurrentStageDisplayName,
    int CompletedNodes,
    int TotalNodes,
    string? FinalAnswer,
    string? ErrorMessage,
    IReadOnlyList<string>? QualityIssues = null,
    IReadOnlyList<ConversationSuggestedActionDto>? SuggestedActions = null);

public sealed record ConversationSuggestedActionDto(
    string Id, string Label, string Prompt, string? LabelEn = null, string? PromptEn = null);
