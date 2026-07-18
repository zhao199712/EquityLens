namespace EquityLens.Api.Data.Entities;

public sealed class AgentWorkflowSetting
{
    public Guid Id { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
