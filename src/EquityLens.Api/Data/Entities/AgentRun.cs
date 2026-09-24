namespace EquityLens.Api.Data.Entities;

public class AgentRun
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ResearchRunId { get; set; }
    public Guid? ParentAgentRunId { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string InputJson { get; set; } = "{}";
    public string? OutputJson { get; set; }
    public string BlackboardJson { get; set; } = "{}";
    public string WorkflowDefinitionJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long OrchestrationVersion { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public bool EnableBlackboardSnapshots { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public decimal TotalEstimatedCostUsd { get; set; }
    public int ExecutionAttempt { get; set; }

    public ICollection<AgentRunNode> Nodes { get; set; } = [];
    public ICollection<AgentRunEvent> Events { get; set; } = [];
    public ICollection<AgentToolCall> ToolCalls { get; set; } = [];
    public ICollection<AgentFeedback> Feedback { get; set; } = [];
    public ICollection<AgentApprovalRequest> Approvals { get; set; } = [];
    public ICollection<AgentRunPromptSnapshot> PromptSnapshots { get; set; } = [];
    public ResearchRun? ResearchRun { get; set; }
    public AgentRun? ParentRun { get; set; }
    public ICollection<AgentRun> ChildRuns { get; set; } = [];
}
