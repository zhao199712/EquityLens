namespace EquityLens.Api.Data.Entities;

public class Citation
{
    public Guid Id { get; set; }
    public Guid AiMemoId { get; set; }
    public Guid? DocumentChunkId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? ReferenceKey { get; set; }
    public string? QuoteText { get; set; }
    public decimal? RelevanceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AiMemo AiMemo { get; set; } = null!;
    public DocumentChunk? DocumentChunk { get; set; }
}
