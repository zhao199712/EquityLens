namespace EquityLens.Api.Data.Entities;

public class RiskMetric
{
    public Guid Id { get; set; }
    public Guid RiskRunId { get; set; }
    public string MetricName { get; set; } = string.Empty; // VaR, CVaR, Volatility, Beta, etc.
    public decimal MetricValue { get; set; }
    public string? Unit { get; set; }
    public decimal? ConfidenceLevel { get; set; }
    public int? HoldingPeriodDays { get; set; }

    // Navigation
    public RiskRun RiskRun { get; set; } = null!;
}
