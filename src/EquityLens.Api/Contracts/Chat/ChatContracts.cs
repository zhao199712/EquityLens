namespace EquityLens.Api.Contracts.Chat;

public sealed record CreateSessionRequest(string? Title = null);

public sealed record CreateSessionResponse(
    Guid Id,
    string? Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int MessageCount);

public sealed record SendMessageRequest(
    string Content);

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
    DateTime CreatedAtUtc);
