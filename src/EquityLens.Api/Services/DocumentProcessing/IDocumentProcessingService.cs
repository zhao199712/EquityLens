namespace EquityLens.Api.Services.DocumentProcessing;

/// <summary>
/// 文件處理管線介面，處理 PDF 解析、分 chunk 與資料庫儲存。
/// </summary>
public interface IDocumentProcessingService
{
    /// <summary>
    /// 處理單筆財報：從 S3 下載 PDF → 解析文字 → 建立 Document + DocumentChunk 記錄。
    /// </summary>
    /// <param name="financialFilingId">財報記錄識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>處理結果。</returns>
    Task<ProcessingResult> ProcessFilingAsync(
        Guid financialFilingId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件處理結果。
/// </summary>
public sealed record ProcessingResult(
    bool Success,
    int PagesProcessed,
    int ChunksCreated,
    string? ErrorMessage);
