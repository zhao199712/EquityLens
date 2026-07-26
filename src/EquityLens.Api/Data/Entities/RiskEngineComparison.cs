namespace EquityLens.Api.Data.Entities;

/// <summary>
/// 同一份 canonical input 在正式 C# 引擎與候選 Python 引擎之間的影子比較紀錄。
/// </summary>
public sealed class RiskEngineComparison
{
    public Guid Id { get; set; }
    public Guid RiskBacktestRunId { get; set; }
    public string PrimaryEngine { get; set; } = "csharp";
    public string PrimaryAlgorithmVersion { get; set; } = string.Empty;
    public string CandidateEngine { get; set; } = "python";
    public string CandidateAlgorithmVersion { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public long? PrimaryDurationMs { get; set; }
    public long? CandidateDurationMs { get; set; }
    public bool? PassedTolerance { get; set; }
    public string? ComparisonJson { get; set; }
    public string? CandidateResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public RiskBacktestRun RiskBacktestRun { get; set; } = null!;
}
