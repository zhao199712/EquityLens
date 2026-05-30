namespace EquityLens.Api.Data.Entities;

public class FinancialReport
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly AsOfDate { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, Generating, Completed, Failed
    public string? Summary { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Portfolio Portfolio { get; set; } = null!;
    public ICollection<AiMemo> AiMemos { get; set; } = [];
    public ICollection<JobRun> JobRuns { get; set; } = [];
}
