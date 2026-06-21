using System.Text.RegularExpressions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.InvestorConferences;

public sealed class ConferenceImportService : IConferenceImportService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IObjectStorageService _objectStorage;

    public ConferenceImportService(EquityLensDbContext dbContext, IObjectStorageService objectStorage)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
    }

    public async Task<ConferenceImportResult> ImportAllAsync(CancellationToken cancellationToken = default)
    {
        var dir = FindConferencesDirectory();
        var pdfFiles = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories);

        var total = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        var securityCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in pdfFiles)
        {
            total++;

            var relDir = Path.GetFileName(Path.GetDirectoryName(filePath)!)!;
            var match = Regex.Match(relDir, @"^(\d+)");
            if (!match.Success)
            {
                Console.WriteLine($"  ✗ {filePath}: 無法解析 ticker");
                failed++;
                continue;
            }
            var ticker = match.Groups[1].Value;

            if (!securityCache.TryGetValue(ticker, out var securityId))
            {
                var security = await _dbContext.Securities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Ticker == ticker, cancellationToken);
                if (security is null)
                {
                    Console.WriteLine($"  ✗ {ticker}: 不在 DB 中，跳過");
                    failed++;
                    continue;
                }
                securityCache[ticker] = securityId = security.Id;
            }

            var fileName = Path.GetFileName(filePath);
            var objectKey = $"investor-conferences/{ticker}/{fileName}";

            var exists = await _dbContext.UploadedFiles.AnyAsync(f => f.ObjectKey == objectKey, cancellationToken);
            if (exists)
            {
                Console.WriteLine($"  → {ticker}/{fileName}: 已存在，跳過");
                skipped++;
                continue;
            }

            var language = DetectLanguage(fileName);
            var sourceUrl = $"{ticker}/{fileName}";

            try
            {
                using var stream = File.OpenRead(filePath);
                await _objectStorage.UploadAsync(objectKey, stream, "application/pdf", cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {ticker}/{fileName}: 上傳失敗 - {ex.Message}");
                failed++;
                continue;
            }

            try
            {
                var fileSize = new FileInfo(filePath).Length;

                var uploadedFile = new UploadedFile
                {
                    Id = Guid.NewGuid(),
                    UploadedByUserId = null,
                    BucketName = "equitylens",
                    ObjectKey = objectKey,
                    OriginalFileName = fileName,
                    ContentType = "application/pdf",
                    FileSizeBytes = fileSize,
                    StorageProvider = "Garage",
                    UploadStatus = "Uploaded",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.UploadedFiles.Add(uploadedFile);

                var document = new Document
                {
                    Id = Guid.NewGuid(),
                    UploadedFileId = uploadedFile.Id,
                    Title = $"{ticker} {securityId} {fileName}",
                    DocumentType = "EarningsPresentation",
                    SourceUrl = sourceUrl,
                    Language = language,
                    ParseStatus = "Pending",
                };
                _dbContext.Documents.Add(document);

                var conference = new InvestorConference
                {
                    Id = Guid.NewGuid(),
                    SecurityId = securityId,
                    UploadedFileId = uploadedFile.Id,
                    DocumentId = document.Id,
                    Title = $"{ticker} 法說會簡報 {fileName}",
                    Language = language,
                    Source = "MOPS",
                    OriginalFileName = fileName,
                    OriginalFileUrl = sourceUrl,
                    ParseStatus = "Pending",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.InvestorConferences.Add(conference);

                await _dbContext.SaveChangesAsync(cancellationToken);
                Console.WriteLine($"  ✓ {ticker}/{fileName}");
                succeeded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {ticker}/{fileName}: DB 寫入失敗 - {ex.Message}");
                failed++;
            }
        }

        return new ConferenceImportResult(total, succeeded, failed, skipped);
    }

    private static string DetectLanguage(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name.Contains('E', StringComparison.OrdinalIgnoreCase) ? "en" : "zh-TW";
    }

    private static string FindConferencesDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "法說會"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "法說會"),
        };
        foreach (var path in candidates)
        {
            var normalized = Path.GetFullPath(path);
            if (Directory.Exists(normalized))
                return normalized;
        }
        throw new DirectoryNotFoundException("找不到法說會目錄，請從專案根目錄執行");
    }
}
