namespace EquityLens.Api.Contracts.Filings;

/// <summary>
/// 財報上傳回應資料。包含財報 metadata 與關聯的檔案資訊。
/// </summary>
public sealed record FinancialFilingResponse(
    Guid Id,
    Guid SecurityId,
    Guid UploadedFileId,
    Guid? DocumentId,
    string FilingType,
    int FiscalYear,
    int? FiscalQuarter,
    DateOnly? PeriodEndDate,
    DateTime? PublishedAt,
    string Language,
    string? Currency,
    string? Source,
    string? SourceUrl,
    string ParseStatus,
    DateTime CreatedAtUtc,
    string OriginalFileName,
    long FileSizeBytes,
    string? ContentType);

/// <summary>
/// 財報列表回應，包含分頁資訊（簡化版，不含 UploadedFile 明細）。
/// </summary>
public sealed record FinancialFilingSummaryResponse(
    Guid Id,
    Guid SecurityId,
    string FilingType,
    int FiscalYear,
    int? FiscalQuarter,
    DateOnly? PeriodEndDate,
    string ParseStatus,
    DateTime CreatedAtUtc,
    string OriginalFileName,
    long FileSizeBytes);
