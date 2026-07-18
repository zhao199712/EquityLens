namespace EquityLens.Api.Data.Entities;

public sealed class AgentNodeSetting
{
    public Guid Id { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? MetadataJson { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? MaxRetryCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
