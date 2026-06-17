namespace EquityLens.Api.Services.FinancialFilings;

/// <summary>
/// TWSE 財報爬蟲介面，從證交所下載上市公司財報 PDF。
/// </summary>
public interface ITwseFilingCrawler
{
    /// <summary>
    /// 查詢指定股票在指定年度的所有可用財報。
    /// </summary>
    /// <param name="stockCode">股票代號，例如 "2330"。</param>
    /// <param name="year">民國年，例如 112。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>可用財報列表。</returns>
    Task<IReadOnlyList<TwseFilingInfo>> GetAvailableFilingsAsync(
        string stockCode, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下載指定財報 PDF 並回傳位元組陣列。
    /// </summary>
    /// <param name="filing">財報資訊。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>PDF 檔案位元組陣列。</returns>
    Task<byte[]> DownloadPdfAsync(
        TwseFilingInfo filing, CancellationToken cancellationToken = default);
}

/// <summary>
/// TWSE 財報檔案資訊。
/// </summary>
public sealed record TwseFilingInfo(
    string StockCode,
    int Year,
    string Quarter,
    string FileName,
    string Description,
    long FileSizeBytes,
    DateTime UploadedAt);
