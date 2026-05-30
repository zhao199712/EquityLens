namespace EquityLens.Api.Data.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid UploadedFileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? SourceUrl { get; set; }
    public string Language { get; set; } = "en";
    public DateTime? PublishedAt { get; set; }
    public DateTime? ParsedAtUtc { get; set; }
    public string ParseStatus { get; set; } = "Pending"; // Pending, Parsing, Completed, Failed

    // Navigation
    public UploadedFile UploadedFile { get; set; } = null!;
    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}
