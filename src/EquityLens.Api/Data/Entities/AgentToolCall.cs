namespace EquityLens.Api.Data.Entities;

public class AgentToolCall
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid? AgentRunNodeId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = "Running"; // Running, Succeeded, Failed, Cancelled
    public string ArgumentsJson { get; set; } = "{}";
    public string? ResultPreview { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMs { get; set; }

    // Navigation
    public AgentRun AgentRun { get; set; } = null!;
    public AgentRunNode? AgentRunNode { get; set; }
}
