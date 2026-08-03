namespace EquityLens.Api.Data.Entities;

/// <summary>將穩定 usage key 綁定至可執行的 prompt 版本。</summary>
public sealed class PromptBinding
{
    public Guid Id { get; set; }
    public string UsageKey { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public Guid PromptTemplateId { get; set; }
    public Guid PromptVersionId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public PromptTemplate Template { get; set; } = null!;
    public PromptVersion Version { get; set; } = null!;
}
