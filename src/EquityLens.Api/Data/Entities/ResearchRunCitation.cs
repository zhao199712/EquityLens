namespace EquityLens.Api.Data.Entities;

public class ResearchRunCitation
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int CitationIndex { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? DocumentChunkId { get; set; }
    public Guid? DocumentId { get; set; }
    public string? Title { get; set; }
    public string? DocumentType { get; set; }
    public string? SourceRole { get; set; }
    public int? PageNumber { get; set; }
    public string? QuoteText { get; set; }
    public double RelevanceScore { get; set; }

    public ResearchRun Run { get; set; } = null!;
}
