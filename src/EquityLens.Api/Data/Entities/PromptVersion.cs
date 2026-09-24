namespace EquityLens.Api.Data.Entities;

/// <summary>不可變的 prompt 版本；僅草稿可編輯。</summary>
public sealed class PromptVersion
{
    public Guid Id { get; set; }
    public Guid PromptTemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public string SystemPrompt { get; set; } = string.Empty;
    public string? UserPrompt { get; set; }
    public string RequiredVariablesJson { get; set; } = "[]";
    public string? ResponseFormat { get; set; }
    public string? ChangeSummary { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public PromptTemplate Template { get; set; } = null!;
    public ICollection<PromptBinding> Bindings { get; set; } = [];
}
