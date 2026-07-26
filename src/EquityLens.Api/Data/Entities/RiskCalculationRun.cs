namespace EquityLens.Api.Data.Entities;

/// <summary>可稽核的跨語言投資組合風險計算工作。</summary>
public sealed class RiskCalculationRun
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string Operation { get; set; } = "risk";
    public string Status { get; set; } = "Queued";
    public int ProgressPercent { get; set; }
    public string RequestedModel { get; set; } = "VT-GARCH-t + Joint-Vector FHS";
    public string? SelectedModel { get; set; }
    public string AlgorithmVersion { get; set; } = "vt-garch-t-joint-fhs-v1";
    public string? InputHash { get; set; }
    public string InputSnapshotJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? FitHealthJson { get; set; }
    public string? DataFactorVersion { get; set; }
    public string? FallbackReason { get; set; }
    public int FallbackDepth { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
}
