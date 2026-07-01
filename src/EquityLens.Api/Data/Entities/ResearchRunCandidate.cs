namespace EquityLens.Api.Data.Entities;

public class ResearchRunCandidate
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string? SearchId { get; set; }
    public string? Query { get; set; }
    public Guid DocumentChunkId { get; set; }
    public Guid? DocumentId { get; set; }
    public string? Title { get; set; }
    public string? DocumentType { get; set; }
    public string? SourceRole { get; set; }
    public int? PageNumber { get; set; }
    public double RelevanceScore { get; set; }
    public double? AdjustedScore { get; set; }
    public int? RankBeforeRerank { get; set; }
    public int? RankAfterRerank { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? DiscardReason { get; set; }
    public string? ContentPreview { get; set; }

    public ResearchRun Run { get; set; } = null!;
}
