namespace EquityLens.Api.Data.Entities;

/// <summary>Prompt 管理操作的 append-only 稽核紀錄。</summary>
public sealed class PromptAuditLog
{
    public Guid Id { get; set; }
    public Guid? PromptTemplateId { get; set; }
    public Guid? PromptVersionId { get; set; }
    public Guid? PromptBindingId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public string? Reason { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
