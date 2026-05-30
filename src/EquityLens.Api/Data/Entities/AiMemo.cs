namespace EquityLens.Api.Data.Entities;

public class AiMemo
{
    public Guid Id { get; set; }
    public Guid? RiskRunId { get; set; }
    public Guid? FinancialReportId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public string? PromptVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public RiskRun? RiskRun { get; set; }
    public FinancialReport? FinancialReport { get; set; }
    public ICollection<Citation> Citations { get; set; } = [];
    public ICollection<CriticNote> CriticNotes { get; set; } = [];
}
