namespace EquityLens.Api.Data.Entities;

public class UploadedFile
{
    public Guid Id { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string BucketName { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Sha256Hash { get; set; }
    public string StorageProvider { get; set; } = "MinIO";
    public string UploadStatus { get; set; } = "Pending"; // Pending, Uploaded, Failed
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser UploadedByUser { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<FinancialFiling> FinancialFilings { get; set; } = [];
    public ICollection<JobRun> JobRuns { get; set; } = [];
}
