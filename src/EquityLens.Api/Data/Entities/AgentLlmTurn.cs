namespace EquityLens.Api.Data.Entities;

public class AgentLlmTurn
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid AgentRunNodeId { get; set; }
    public int Turn { get; set; }
    public int ExecutionAttempt { get; set; }
    public string Mode { get; set; } = "Research";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string PromptTemplateId { get; set; } = "research-agent-loop";
    public int PromptVersion { get; set; } = 1;
    public string RenderedPromptHash { get; set; } = "";
    public string Action { get; set; } = "";
    public string ThoughtSummary { get; set; } = "";
    public string ActionJson { get; set; } = "{}";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AgentLoopCheckpoint
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid AgentRunNodeId { get; set; }
    public int Turn { get; set; }
    public int ExecutionAttempt { get; set; }
    public long LeaseVersion { get; set; }
    public string StateJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; }
}
