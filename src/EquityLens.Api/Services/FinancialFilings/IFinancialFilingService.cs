using EquityLens.Api.Contracts.Filings;

namespace EquityLens.Api.Services.FinancialFilings;

/// <summary>
/// 財報上傳服務介面，負責財報檔案上傳、查詢與刪除的業務邏輯。
/// </summary>
public interface IFinancialFilingService
{
    /// <summary>
    /// 上傳財報檔案並建立財報記錄。
    /// </summary>
    /// <param name="file">要上傳的財報檔案。</param>
    /// <param name="securityId">關聯的股票識別碼。</param>
    /// <param name="fiscalYear">財報年度。</param>
    /// <param name="fiscalQuarter">財報季度（年報可為 null）。</param>
    /// <param name="filingType">財報類型（10-K, 10-Q, Annual, Quarterly 等）。</param>
    /// <param name="periodEndDate">報表截止日。</param>
    /// <param name="publishedAt">發佈日期。</param>
    /// <param name="language">語言。</param>
    /// <param name="currency">幣別。</param>
    /// <param name="source">資料來源。</param>
    /// <param name="sourceUrl">原始來源網址。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>已建立的財報記錄回應資料。</returns>
    Task<FinancialFilingResponse> UploadAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查詢財報列表，可依股票、年度、類型篩選。
    /// </summary>
    /// <param name="securityId">選擇性 - 依股票篩選。</param>
    /// <param name="fiscalYear">選擇性 - 依年度篩選。</param>
    /// <param name="filingType">選擇性 - 依類型篩選。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>財報摘要列表。</returns>
    Task<IReadOnlyList<FinancialFilingSummaryResponse>> ListAsync(
        Guid? securityId = null,
        int? fiscalYear = null,
        string? filingType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依識別碼取得單筆財報記錄。
    /// </summary>
    /// <param name="id">財報記錄的識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>財報記錄回應資料；若不存在則返回 null。</returns>
    Task<FinancialFilingResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刪除指定財報記錄（包含物件儲存檔案與資料庫記錄）。
    /// </summary>
    /// <param name="id">財報記錄的識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>是否成功刪除；若不存在則返回 false。</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
