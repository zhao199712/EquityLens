namespace EquityLens.Api.Data.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatSessionId { get; set; }
    public string Role { get; set; } = string.Empty; // user, assistant, tool, system
    public string? Content { get; set; }
    public string? ToolName { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolCallsJson { get; set; }
    public string MessageType { get; set; } = "Text";
    public Guid? ConversationTurnId { get; set; }
    public Guid? AgentRunId { get; set; }
    public int? SequenceNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ChatSession ChatSession { get; set; } = null!;
    public ConversationTurn? ConversationTurn { get; set; }
    public AgentRun? AgentRun { get; set; }
}
