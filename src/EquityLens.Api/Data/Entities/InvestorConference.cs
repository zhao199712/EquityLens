namespace EquityLens.Api.Data.Entities;

public class InvestorConference
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public Guid? UploadedFileId { get; set; }
    public Guid? DocumentId { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime? EventDate { get; set; }
    public string? Location { get; set; }
    public string? Summary { get; set; }
    public string Language { get; set; } = "zh-TW";
    public string? Source { get; set; }
    public string? SourceUrl { get; set; }
    public string? OriginalFileName { get; set; }
    public string? OriginalFileUrl { get; set; }

    public string ParseStatus { get; set; } = "Pending";
    // Pending, Parsing, Completed, Failed

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Security Security { get; set; } = null!;
    public UploadedFile? UploadedFile { get; set; }
    public Document? Document { get; set; }
}
