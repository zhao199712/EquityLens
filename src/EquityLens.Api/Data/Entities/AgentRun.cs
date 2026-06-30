namespace EquityLens.Api.Data.Entities;

public class AgentRun
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Running, WaitingForFeedback, Succeeded, Failed, Cancelled
    public string InputJson { get; set; } = "{}";
    public string? OutputJson { get; set; }
    public string BlackboardJson { get; set; } = "{}";
    public string WorkflowDefinitionJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
    public ICollection<AgentRunNode> Nodes { get; set; } = [];
    public ICollection<AgentRunEvent> Events { get; set; } = [];
    public ICollection<AgentToolCall> ToolCalls { get; set; } = [];
    public ICollection<AgentFeedback> Feedbacks { get; set; } = [];
}
