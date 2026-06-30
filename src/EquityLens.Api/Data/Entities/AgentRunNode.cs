namespace EquityLens.Api.Data.Entities;

public class AgentRunNode
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Ready, Running, Succeeded, Failed, Skipped, WaitingForFeedback
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMs { get; set; }

    // Navigation
    public AgentRun AgentRun { get; set; } = null!;
}
