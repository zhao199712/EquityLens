namespace EquityLens.Api.Data.Entities;

/// <summary>
/// Immutable request and result snapshot for an expensive portfolio backtest.
/// The linked JobRun represents queue execution; this record remains available after it completes.
/// </summary>
public sealed class RiskBacktestRun
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid JobRunId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int LookbackDays { get; set; } = 252;
    public int Simulations { get; set; } = 5000;
    public string AlgorithmVersion { get; set; } = "mvewma-fhs-backtest-v2";
    public string Status { get; set; } = "Queued";
    public int ProgressPercent { get; set; }
    public string InputSnapshotJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
}
