namespace EquityLens.Api.Services.FinancialData;

/// <summary>
/// 將已下載的 TWSE 年報 PDF 上傳到 Garage 並寫入 DB metadata。
/// </summary>
public interface ITwseReportFileImportService
{
    /// <summary>
    /// 匯入所有年報 PDF 到 Garage 與資料庫。
    /// </summary>
    Task<TwseReportFileImportResult> ImportAllAsync(CancellationToken cancellationToken = default);
}

public sealed record TwseReportFileImportResult(
    int Total,
    int Succeeded,
    int Failed,
    int Skipped);
