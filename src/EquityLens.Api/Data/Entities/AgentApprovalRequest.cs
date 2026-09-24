namespace EquityLens.Api.Data.Entities;

public sealed class AgentApprovalRequest
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid AgentRunNodeId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string SideEffectLevel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PolicySnapshotJson { get; set; } = "{}";
    public int ExecutionAttempt { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public Guid? ClientRequestId { get; set; }
    public string? DecisionComment { get; set; }

    public AgentRun Run { get; set; } = null!;
    public AgentRunNode Node { get; set; } = null!;
}
