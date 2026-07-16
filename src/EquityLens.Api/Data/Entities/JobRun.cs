namespace EquityLens.Api.Data.Entities;

public class JobRun
{
    public Guid Id { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? RiskRunId { get; set; }
    public Guid? FinancialReportId { get; set; }
    public Guid? UploadedFileId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued"; // Queued, Running, Completed, Failed, Cancelled
    public int ProgressPercent { get; set; }
    public string? RedisJobId { get; set; }
    public string? CorrelationId { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser? CreatedByUser { get; set; }
    public RiskRun? RiskRun { get; set; }
    public FinancialReport? FinancialReport { get; set; }
    public UploadedFile? UploadedFile { get; set; }
}
