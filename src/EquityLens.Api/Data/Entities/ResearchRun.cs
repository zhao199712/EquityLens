namespace EquityLens.Api.Data.Entities;

public class ResearchRun
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Status { get; set; } = "Answered";
    public string? Model { get; set; }
    public string RetrievalMode { get; set; } = string.Empty;
    public string SourcePolicy { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public int TopK { get; set; }
    public double Temperature { get; set; }
    public int CitationCount { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ResearchRunStep> Steps { get; set; } = [];
    public ICollection<ResearchRunCandidate> Candidates { get; set; } = [];
    public ICollection<ResearchRunCitation> Citations { get; set; } = [];
}
