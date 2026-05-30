namespace EquityLens.Api.Data.Entities;

public class RiskRun
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid RiskModelSettingId { get; set; }
    public string? RunName { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InputHash { get; set; }

    // Navigation
    public Portfolio Portfolio { get; set; } = null!;
    public RiskModelSetting RiskModelSetting { get; set; } = null!;
    public ICollection<RiskMetric> Metrics { get; set; } = [];
    public ICollection<ScenarioResult> ScenarioResults { get; set; } = [];
    public ICollection<AiMemo> AiMemos { get; set; } = [];
    public ICollection<JobRun> JobRuns { get; set; } = [];
}
