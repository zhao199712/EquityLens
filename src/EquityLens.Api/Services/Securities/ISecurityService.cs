using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Securities;

/// <summary>
/// 證券資料服務介面，提供證券查詢、搜尋、建立與解析等功能。
/// </summary>
public interface ISecurityService
{
    /// <summary>
    /// 依據關鍵字搜尋本地已存在的證券。
    /// </summary>
    /// <param name="query">搜尋關鍵字，可為 null 或空白。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>符合條件的證券列表。</returns>
    Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken);

    /// <summary>
    /// 搜尋本地與外部資料來源可用的證券，並合併去重後返回。
    /// </summary>
    /// <param name="query">搜尋關鍵字，可為 null 或空白。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>合併後的證券搜尋結果列表，最多返回 25 筆。</returns>
    Task<IReadOnlyList<SecuritySearchResult>> SearchAvailableAsync(string? query, CancellationToken cancellationToken);

    /// <summary>
    /// 依據證券識別碼取得證券詳細資料。
    /// </summary>
    /// <param name="id">證券的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回證券詳細資料；
    /// 若證券不存在則返回錯誤碼 <c>security.not_found</c>。
    /// </returns>
    Task<Result<SecurityResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// 解析證券資料：若本地已存在則返回現有資料（metadata 過期時自動刷新），
    /// 否則查詢外部 API 並建立新證券。
    /// </summary>
    /// <param name="request">解析證券的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回解析結果（包含是否為新建立）；
    /// 若必填欄位缺失則返回錯誤碼 <c>security.required_fields</c>。
    /// </returns>
    Task<Result<ResolveSecurityResponse>> ResolveAsync(ResolveSecurityRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 確保證券存在：依據識別碼或代號/交易所查詢，若不存在則透過外部 API 建立新證券（不自動儲存）。
    /// </summary>
    /// <param name="request">確保證券的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回證券實體；
    /// 若證券不存在且缺少必要資訊則返回錯誤碼 <c>security.required_fields</c>。
    /// </returns>
    Task<Result<Security>> EnsureAsync(EnsureSecurityRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 刷新資料庫中既有證券的 metadata，從外部 API 取得最新資料並更新。
    /// </summary>
    /// <param name="force">是否強制刷新所有證券，不論是否過期。</param>
    /// <param name="limit">最多處理的證券數量。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>刷新結果統計。</returns>
    Task<RefreshSecuritiesResponse> RefreshAllAsync(bool force, int limit, CancellationToken cancellationToken);
}
