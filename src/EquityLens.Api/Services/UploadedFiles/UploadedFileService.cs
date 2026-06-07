using System.Security.Cryptography;
using EquityLens.Api.Contracts.Files;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.UploadedFiles;

/// <summary>
/// 已上傳檔案服務實現，處理物件儲存上傳與資料庫記錄。
/// </summary>
public sealed class UploadedFileService : IUploadedFileService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IDemoUserContext _demoUserContext;
    private readonly IObjectStorageService _objectStorage;

    /// <summary>
    /// 初始化已上傳檔案服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="demoUserContext">演示使用者內容。</param>
    /// <param name="objectStorage">物件儲存服務。</param>
    public UploadedFileService(
        EquityLensDbContext dbContext,
        IDemoUserContext demoUserContext,
        IObjectStorageService objectStorage)
    {
        _dbContext = dbContext;
        _demoUserContext = demoUserContext;
        _objectStorage = objectStorage;
    }

    /// <inheritdoc />
    public async Task<UploadedFileResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var userId = _demoUserContext.UserId;
        var objectKey = BuildObjectKey(userId, file.FileName);

        // 計算 SHA256 雜湊並上傳至物件儲存
        string sha256Hash;
        using (var hashStream = file.OpenReadStream())
        {
            sha256Hash = await ComputeSha256Async(hashStream, cancellationToken);
        }

        using (var uploadStream = file.OpenReadStream())
        {
            await _objectStorage.UploadAsync(objectKey, uploadStream, file.ContentType, cancellationToken);
        }

        var uploadedFile = new UploadedFile
        {
            Id = Guid.NewGuid(),
            UploadedByUserId = userId,
            BucketName = "equitylens",
            ObjectKey = objectKey,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Sha256Hash = sha256Hash,
            StorageProvider = "Garage",
            UploadStatus = "Uploaded",
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.UploadedFiles.Add(uploadedFile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(uploadedFile);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UploadedFileResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var files = await _dbContext.UploadedFiles
            .AsNoTracking()
            .Where(x => x.UploadedByUserId == _demoUserContext.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return files.Select(ToResponse).ToList();
    }

    /// <inheritdoc />
    public async Task<UploadedFileResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var file = await _dbContext.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UploadedByUserId == _demoUserContext.UserId, cancellationToken);

        return file is null ? null : ToResponse(file);
    }

    /// <inheritdoc />
    public async Task<PresignedUrlResponse?> CreateDownloadUrlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var file = await _dbContext.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UploadedByUserId == _demoUserContext.UserId, cancellationToken);

        if (file is null)
        {
            return null;
        }

        var url = await _objectStorage.CreatePresignedDownloadUrlAsync(file.ObjectKey, cancellationToken: cancellationToken);
        var expiryMinutes = 15; // 與 ObjectStorageOptions 預設一致
        return new PresignedUrlResponse(url, DateTime.UtcNow.AddMinutes(expiryMinutes));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var file = await _dbContext.UploadedFiles
            .FirstOrDefaultAsync(x => x.Id == id && x.UploadedByUserId == _demoUserContext.UserId, cancellationToken);

        if (file is null)
        {
            return false;
        }

        await _objectStorage.DeleteAsync(file.ObjectKey, cancellationToken);
        _dbContext.UploadedFiles.Remove(file);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    // 組合物件儲存金鑰：uploads/{userId}/{yyyy}/{MM}/{guid}-{safeFileName}
    private static string BuildObjectKey(Guid userId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName).Replace(" ", "_");
        var now = DateTime.UtcNow;
        return $"uploads/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}-{safeFileName}";
    }

    // 計算資料流的 SHA256 雜湊值，以十六進位字串表示
    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // 將 UploadedFile 實體轉換為回應資料
    private static UploadedFileResponse ToResponse(UploadedFile file)
    {
        return new UploadedFileResponse(
            file.Id,
            file.OriginalFileName,
            file.ContentType,
            file.FileSizeBytes,
            file.ObjectKey,
            file.UploadStatus,
            file.CreatedAtUtc);
    }
}
