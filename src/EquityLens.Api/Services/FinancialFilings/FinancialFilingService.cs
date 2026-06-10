using EquityLens.Api.Contracts.Filings;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.UploadedFiles;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.FinancialFilings;

/// <summary>
/// 財報上傳服務實現，處理物件儲存上傳、資料庫記錄建立與查詢。
/// </summary>
public sealed class FinancialFilingService : IFinancialFilingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IObjectStorageService _objectStorage;

    public FinancialFilingService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IObjectStorageService objectStorage)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _objectStorage = objectStorage;
    }

    /// <inheritdoc />
    public async Task<FinancialFilingResponse> UploadAsync(
        IFormFile file,
        Guid securityId,
        int fiscalYear,
        int? fiscalQuarter,
        string filingType,
        DateOnly? periodEndDate,
        DateTime? publishedAt,
        string? language,
        string? currency,
        string? source,
        string? sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var objectKey = BuildObjectKey(userId, file.FileName);

        // 上傳至 S3 物件儲存
        using (var uploadStream = file.OpenReadStream())
        {
            await _objectStorage.UploadAsync(objectKey, uploadStream, file.ContentType, cancellationToken);
        }

        // 建立 UploadedFile 記錄
        var uploadedFile = new UploadedFile
        {
            Id = Guid.NewGuid(),
            UploadedByUserId = userId,
            BucketName = "equitylens",
            ObjectKey = objectKey,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            StorageProvider = "Garage",
            UploadStatus = "Uploaded",
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.UploadedFiles.Add(uploadedFile);

        // 建立 FinancialFiling 記錄
        var filing = new FinancialFiling
        {
            Id = Guid.NewGuid(),
            SecurityId = securityId,
            UploadedFileId = uploadedFile.Id,
            UploadedByUserId = userId,
            FilingType = filingType,
            FiscalYear = fiscalYear,
            FiscalQuarter = fiscalQuarter,
            PeriodEndDate = periodEndDate,
            PublishedAt = publishedAt,
            Language = language ?? "en",
            Currency = currency,
            Source = source,
            SourceUrl = sourceUrl,
            ParseStatus = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Add(filing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(filing, uploadedFile);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinancialFilingSummaryResponse>> ListAsync(
        Guid? securityId = null,
        int? fiscalYear = null,
        string? filingType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.FinancialFilings
            .AsNoTracking()
            .Include(f => f.UploadedFile)
            .Where(f => f.UploadedByUserId == _currentUser.UserId);

        if (securityId.HasValue)
            query = query.Where(f => f.SecurityId == securityId.Value);

        if (fiscalYear.HasValue)
            query = query.Where(f => f.FiscalYear == fiscalYear.Value);

        if (!string.IsNullOrEmpty(filingType))
            query = query.Where(f => f.FilingType == filingType);

        var filings = await query
            .OrderByDescending(f => f.FiscalYear)
            .ThenByDescending(f => f.FiscalQuarter)
            .ThenByDescending(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return filings.Select(f => ToSummaryResponse(f, f.UploadedFile)).ToList();
    }

    /// <inheritdoc />
    public async Task<FinancialFilingResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filing = await _dbContext.FinancialFilings
            .AsNoTracking()
            .Include(f => f.UploadedFile)
            .FirstOrDefaultAsync(f => f.Id == id && f.UploadedByUserId == _currentUser.UserId, cancellationToken);

        if (filing is null)
            return null;

        return ToResponse(filing, filing.UploadedFile);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filing = await _dbContext.FinancialFilings
            .Include(f => f.UploadedFile)
            .FirstOrDefaultAsync(f => f.Id == id && f.UploadedByUserId == _currentUser.UserId, cancellationToken);

        if (filing is null)
            return false;

        // 刪除 S3 物件
        await _objectStorage.DeleteAsync(filing.UploadedFile.ObjectKey, cancellationToken);

        // 移除 UploadedFile（Cascade 會自動刪除 FinancialFiling）
        _dbContext.UploadedFiles.Remove(filing.UploadedFile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string BuildObjectKey(Guid userId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName).Replace(" ", "_");
        var now = DateTime.UtcNow;
        return $"filings/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}-{safeFileName}";
    }

    private static FinancialFilingResponse ToResponse(FinancialFiling filing, UploadedFile uploadedFile)
    {
        return new FinancialFilingResponse(
            filing.Id,
            filing.SecurityId,
            filing.UploadedFileId,
            filing.DocumentId,
            filing.FilingType,
            filing.FiscalYear,
            filing.FiscalQuarter,
            filing.PeriodEndDate,
            filing.PublishedAt,
            filing.Language,
            filing.Currency,
            filing.Source,
            filing.SourceUrl,
            filing.ParseStatus,
            filing.CreatedAtUtc,
            uploadedFile.OriginalFileName,
            uploadedFile.FileSizeBytes,
            uploadedFile.ContentType);
    }

    private static FinancialFilingSummaryResponse ToSummaryResponse(FinancialFiling filing, UploadedFile uploadedFile)
    {
        return new FinancialFilingSummaryResponse(
            filing.Id,
            filing.SecurityId,
            filing.FilingType,
            filing.FiscalYear,
            filing.FiscalQuarter,
            filing.PeriodEndDate,
            filing.ParseStatus,
            filing.CreatedAtUtc,
            uploadedFile.OriginalFileName,
            uploadedFile.FileSizeBytes);
    }
}
