namespace EquityLens.Api.Data.Entities;

public class FinancialFiling
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public Guid UploadedFileId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? UploadedByUserId { get; set; }

    public string FilingType { get; set; } = string.Empty;
    // AnnualReport, QuarterlyReport, 10-K, 10-Q, 20-F, EarningsRelease

    public int FiscalYear { get; set; }
    public int? FiscalQuarter { get; set; }

    public DateOnly? PeriodEndDate { get; set; }
    public DateTime? PublishedAt { get; set; }

    public string Language { get; set; } = "en";
    public string? Currency { get; set; }
    public string? Source { get; set; }
    public string? SourceUrl { get; set; }

    public string ParseStatus { get; set; } = "Pending";
    // Pending, Parsing, Completed, Failed

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Security Security { get; set; } = null!;
    public UploadedFile UploadedFile { get; set; } = null!;
    public Document? Document { get; set; }
    public AppUser? UploadedByUser { get; set; }
}
