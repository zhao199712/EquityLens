namespace EquityLens.Api.Data.Entities;

public class ConversationTurn
{
    public Guid Id { get; set; }
    public Guid ChatSessionId { get; set; }
    public Guid RequestId { get; set; }
    public string Status { get; set; } = "Processing";
    public string? Action { get; set; }
    public string? ReasonCode { get; set; }
    public string? StandaloneQuery { get; set; }
    public long InputContextVersion { get; set; }
    public long? OutputContextVersion { get; set; }
    public Guid? AgentRunId { get; set; }
    public Guid? ResearchRunId { get; set; }
    public string? Model { get; set; }
    public string? Provider { get; set; }
    public string PromptTemplateId { get; set; } = "conversation-router";
    public int PromptVersion { get; set; } = 1;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public ChatSession ChatSession { get; set; } = null!;
    public AgentRun? AgentRun { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = [];
}
