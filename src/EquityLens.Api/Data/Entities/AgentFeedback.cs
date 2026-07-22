namespace EquityLens.Api.Data.Entities;

public class AgentFeedback
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid? AgentRunNodeId { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? FollowUpAgentRunId { get; set; }
    public string FeedbackType { get; set; } = string.Empty;
    public string Status { get; set; } = "Requested";
    public string Prompt { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAtUtc { get; set; }

    public AgentRun Run { get; set; } = null!;
    public AgentRunNode? Node { get; set; }
    public AgentRun? FollowUpRun { get; set; }
}
