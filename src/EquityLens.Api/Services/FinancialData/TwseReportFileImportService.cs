using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.FinancialData;

public sealed class TwseReportFileImportService : ITwseReportFileImportService
{
    private static readonly Regex FilePattern = new(
        @"^(\d{4})_(\d{3})_annual\.pdf$", RegexOptions.Compiled);

    private readonly EquityLensDbContext _dbContext;
    private readonly IObjectStorageService _objectStorage;

    public TwseReportFileImportService(
        EquityLensDbContext dbContext,
        IObjectStorageService objectStorage)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
    }

    public async Task<TwseReportFileImportResult> ImportAllAsync(CancellationToken cancellationToken = default)
    {
        var reportsDir = FindReportsDirectory();
        var tickerDirs = Directory.GetDirectories(reportsDir);

        var total = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        var securityCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var tickerDir in tickerDirs)
        {
            var ticker = Path.GetFileName(tickerDir);

            if (!securityCache.TryGetValue(ticker, out var securityId))
            {
                var security = await _dbContext.Securities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Ticker == ticker, cancellationToken);
                if (security is null)
                {
                    Console.WriteLine($"  ✗ {ticker}: 不在 DB 中，跳過");
                    continue;
                }
                securityCache[ticker] = securityId = security.Id;
            }

            var pdfFiles = Directory.GetFiles(tickerDir, "*_annual.pdf");
            foreach (var filePath in pdfFiles)
            {
                total++;

                var fileName = Path.GetFileName(filePath);
                var match = FilePattern.Match(fileName);
                if (!match.Success)
                {
                    Console.WriteLine($"  ✗ {filePath}: 檔名格式不符，跳過");
                    failed++;
                    continue;
                }

            var rocYear = int.Parse(match.Groups[2].Value);
            var fiscalYear = rocYear + 1911;

                var objectKey = $"financial-reports/twse/{ticker}/{fiscalYear}/annual-report.pdf";

                var exists = await _dbContext.UploadedFiles.AnyAsync(
                    f => f.ObjectKey == objectKey, cancellationToken);
                if (exists)
                {
                    Console.WriteLine($"  → {ticker}/{fiscalYear}: 已存在，跳過");
                    skipped++;
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(filePath);
                    await _objectStorage.UploadAsync(objectKey, stream, "application/pdf", cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ {ticker}/{fiscalYear}: 上傳 Garage 失敗 - {ex.Message}");
                    failed++;
                    continue;
                }

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var sha256 = await ComputeSha256Async(filePath, cancellationToken);
                    var periodEndDate = new DateOnly(fiscalYear, 12, 31);

                    var uploadedFile = new UploadedFile
                    {
                        Id = Guid.NewGuid(),
                        UploadedByUserId = null,
                        BucketName = "equitylens",
                        ObjectKey = objectKey,
                        OriginalFileName = fileName,
                        ContentType = "application/pdf",
                        FileSizeBytes = fileInfo.Length,
                        Sha256Hash = sha256,
                        StorageProvider = "Garage",
                        UploadStatus = "Uploaded",
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _dbContext.UploadedFiles.Add(uploadedFile);

                    var document = new Document
                    {
                        Id = Guid.NewGuid(),
                        UploadedFileId = uploadedFile.Id,
                        Title = $"{ticker} Annual Report {fiscalYear}",
                        DocumentType = "AnnualReport",
                        SourceUrl = $"https://doc.twse.com.tw/server-java/t57sb01?co_id={ticker}",
                        Language = "zh-TW",
                        ParseStatus = "Pending"
                    };
                    _dbContext.Documents.Add(document);

                    var filing = new FinancialFiling
                    {
                        Id = Guid.NewGuid(),
                        SecurityId = securityId,
                        UploadedFileId = uploadedFile.Id,
                        DocumentId = document.Id,
                        FilingType = "AnnualReport",
                        FiscalYear = fiscalYear,
                        FiscalQuarter = null,
                        PeriodEndDate = periodEndDate,
                        Language = "zh-TW",
                        Currency = "TWD",
                        Source = "TWSE",
                        SourceUrl = objectKey,
                        ParseStatus = "Pending",
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _dbContext.FinancialFilings.Add(filing);

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    Console.WriteLine($"  ✓ {ticker}/{fiscalYear}");
                    succeeded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ {ticker}/{fiscalYear}: DB 寫入失敗 - {ex.Message}");
                    failed++;

                    try
                    {
                        await _objectStorage.DeleteAsync(objectKey, cancellationToken);
                    }
                    catch
                    {
                    }
                }
            }
        }

        return new TwseReportFileImportResult(total, succeeded, failed, skipped);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string FindReportsDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "exports", "financial-reports"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "exports", "financial-reports"),
        };
        foreach (var path in candidates)
        {
            var normalized = Path.GetFullPath(path);
            if (Directory.Exists(normalized))
                return normalized;
        }
        throw new DirectoryNotFoundException("找不到 financial-reports 目錄，請從專案根目錄執行");
    }
}
