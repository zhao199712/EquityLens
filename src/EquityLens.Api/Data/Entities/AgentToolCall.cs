namespace EquityLens.Api.Data.Entities;

public class AgentToolCall
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid? AgentRunNodeId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public string ArgumentsJson { get; set; } = "{}";
    public string? ResultPreview { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMs { get; set; }
    public Guid? PromptSnapshotId { get; set; }
    public string? RenderedPromptHash { get; set; }

    public AgentRun Run { get; set; } = null!;
    public AgentRunNode? Node { get; set; }
    public AgentRunPromptSnapshot? PromptSnapshot { get; set; }
}
