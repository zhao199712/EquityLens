using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.DocumentParsing;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.DocumentProcessing;

/// <summary>
/// 文件處理管線實現，處理 PDF 下載、解析、分 chunk 與資料庫儲存。
/// </summary>
public sealed class DocumentProcessingService : IDocumentProcessingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IPdfParser _pdfParser;
    private readonly IObjectStorageService _objectStorage;

    /// <summary>
    /// 初始化文件處理管線服務。
    /// </summary>
    public DocumentProcessingService(
        EquityLensDbContext dbContext,
        IPdfParser pdfParser,
        IObjectStorageService objectStorage)
    {
        _dbContext = dbContext;
        _pdfParser = pdfParser;
        _objectStorage = objectStorage;
    }

    /// <inheritdoc />
    public async Task<ProcessingResult> ProcessFilingAsync(
        Guid financialFilingId, CancellationToken cancellationToken = default)
    {
        var filing = await _dbContext.FinancialFilings
            .Include(f => f.UploadedFile)
            .FirstOrDefaultAsync(f => f.Id == financialFilingId, cancellationToken);

        if (filing is null)
            return new ProcessingResult(false, 0, 0, "財報記錄不存在。");

        if (filing.UploadedFile is null)
            return new ProcessingResult(false, 0, 0, "關聯檔案不存在。");

        try
        {
            // 更新狀態為解析中
            filing.ParseStatus = "Parsing";
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 下載 PDF（支援本地和 S3）
            byte[] pdfBytes;
            if (filing.UploadedFile.StorageProvider == "Local")
            {
                pdfBytes = await System.IO.File.ReadAllBytesAsync(
                    filing.UploadedFile.ObjectKey, cancellationToken);
            }
            else
            {
                using var downloadStream = await _objectStorage.DownloadAsync(
                    filing.UploadedFile.ObjectKey, cancellationToken);
                using var ms = new MemoryStream();
                await downloadStream.CopyToAsync(ms, cancellationToken);
                pdfBytes = ms.ToArray();
            }

            // 解析 PDF 逐頁文字
            var pages = _pdfParser.Parse(pdfBytes);

            // 建立 Document 記錄
            var document = new Document
            {
                Id = Guid.NewGuid(),
                UploadedFileId = filing.UploadedFileId,
                Title = filing.UploadedFile.OriginalFileName,
                DocumentType = "FinancialReport",
                SourceUrl = filing.SourceUrl,
                Language = filing.Language,
                PublishedAt = filing.PublishedAt,
                ParsedAtUtc = DateTime.UtcNow,
                ParseStatus = "Completed"
            };

            _dbContext.Documents.Add(document);

            // 逐頁建立 DocumentChunk 記錄
            var chunks = new List<DocumentChunk>();
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (string.IsNullOrWhiteSpace(page.Content))
                    continue;

                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    ChunkIndex = i,
                    Content = page.Content,
                    TokenCount = page.TokenCount,
                    PageNumber = page.PageNumber,
                    SectionTitle = $"Page {page.PageNumber}",
                    ContentHash = ComputeHash(page.Content),
                    CreatedAtUtc = DateTime.UtcNow
                };

                chunks.Add(chunk);
                _dbContext.DocumentChunks.Add(chunk);
            }

            // 更新 FinancialFiling 關聯
            filing.DocumentId = document.Id;
            filing.ParseStatus = "Completed";

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProcessingResult(true, pages.Count, chunks.Count, null);
        }
        catch (Exception ex)
        {
            // 解析失敗時更新狀態
            filing.ParseStatus = "Failed";
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProcessingResult(false, 0, 0, ex.Message);
        }
    }

    /// <summary>
    /// 計算內容雜湊值，用於去重。
    /// </summary>
    private static string ComputeHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
