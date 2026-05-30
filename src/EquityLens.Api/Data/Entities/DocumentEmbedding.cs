using Pgvector;

namespace EquityLens.Api.Data.Entities;

public class DocumentEmbedding
{
    public Guid Id { get; set; }
    public Guid DocumentChunkId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string EmbeddingModel { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public DocumentChunk DocumentChunk { get; set; } = null!;
}
