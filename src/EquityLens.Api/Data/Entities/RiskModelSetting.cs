namespace EquityLens.Api.Data.Entities;

public class RiskModelSetting
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int LookbackDays { get; set; } = 252;
    public decimal ConfidenceLevel { get; set; } = 0.95m;
    public int HoldingPeriodDays { get; set; } = 1;
    public string Method { get; set; } = "Historical"; // Historical, Parametric, MonteCarlo
    public string ReturnType { get; set; } = "Log"; // Log, Simple
    public bool IsDefault { get; set; }

    // Navigation
    public ICollection<RiskRun> RiskRuns { get; set; } = [];
}
