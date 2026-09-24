namespace EquityLens.Api.Data.Entities;

/// <summary>Run 建立時凍結的 prompt 內容與版本收據。</summary>
public sealed class AgentRunPromptSnapshot
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public string UsageKey { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public Guid PromptTemplateId { get; set; }
    public string PromptTemplateKey { get; set; } = string.Empty;
    public Guid PromptVersionId { get; set; }
    public int PromptVersionNumber { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string? UserPrompt { get; set; }
    public string RequiredVariablesJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public DateTime ResolvedAtUtc { get; set; } = DateTime.UtcNow;
    public AgentRun Run { get; set; } = null!;
    public ICollection<AgentToolCall> ToolCalls { get; set; } = [];
}
