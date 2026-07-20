namespace EquityLens.Api.Data.Entities;

public class AgentRunNode
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMs { get; set; }
    public string TemplateNodeKey { get; set; } = string.Empty;
    public int Iteration { get; set; }

    public AgentRun Run { get; set; } = null!;
    public ICollection<AgentRunEvent> Events { get; set; } = [];
    public ICollection<AgentToolCall> ToolCalls { get; set; } = [];
    public ICollection<AgentFeedback> Feedback { get; set; } = [];
}
