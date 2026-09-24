namespace EquityLens.Api.Data.Entities;

public sealed class RiskQualityEvaluation
{
    public Guid Id { get; set; }
    public Guid RiskCalculationRunId { get; set; }
    public Guid RiskBacktestRunId { get; set; }
    public string PolicyVersion { get; set; } = "risk-quality-v1";
    public string Status { get; set; } = "Pending";
    public string Model { get; set; } = string.Empty;
    public decimal ConfidenceLevel { get; set; } = .95m;
    public int? ObservationCount { get; set; }
    public decimal? KupiecPValue { get; set; }
    public decimal? ChristoffersenPValue { get; set; }
    public string? EsStatus { get; set; }
    public bool? FitHealthy { get; set; }
    public string FailureCodesJson { get; set; } = "[]";
    public string WarningCodesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EvaluatedAtUtc { get; set; }
    public RiskCalculationRun RiskCalculationRun { get; set; } = null!;
    public RiskBacktestRun RiskBacktestRun { get; set; } = null!;
}
