namespace EquityLens.Api.Data.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? TokenCount { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public string? ContentHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Document Document { get; set; } = null!;
    public DocumentEmbedding? Embedding { get; set; }
    public ICollection<Citation> Citations { get; set; } = [];
}
