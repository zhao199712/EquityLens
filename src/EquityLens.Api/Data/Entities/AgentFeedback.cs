namespace EquityLens.Api.Data.Entities;

public class AgentFeedback
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid? AgentRunNodeId { get; set; }
    public string FeedbackType { get; set; } = string.Empty;
    public string Status { get; set; } = "Requested"; // Requested, Submitted, Expired, Cancelled
    public string Prompt { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAtUtc { get; set; }

    // Navigation
    public AgentRun AgentRun { get; set; } = null!;
    public AgentRunNode? AgentRunNode { get; set; }
}
