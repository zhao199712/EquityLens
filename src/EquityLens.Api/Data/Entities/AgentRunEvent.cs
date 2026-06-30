namespace EquityLens.Api.Data.Entities;

public class AgentRunEvent
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid? AgentRunNodeId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AgentRun AgentRun { get; set; } = null!;
    public AgentRunNode? AgentRunNode { get; set; }
}
