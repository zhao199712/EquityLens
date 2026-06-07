using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;

namespace EquityLens.Api.Services.Portfolios;

/// <summary>
/// 投資組合服務介面，提供投資組合的查詢、建立、更新與刪除功能。
/// </summary>
public interface IPortfolioService
{
    /// <summary>
    /// 取得當前使用者的所有活躍投資組合列表。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>投資組合摘要列表。</returns>
    Task<IReadOnlyList<PortfolioListItemResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 依據投資組合識別碼取得詳細資料。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回投資組合詳細資料；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>。
    /// </returns>
    Task<Result<PortfolioDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// 建立新的投資組合。
    /// </summary>
    /// <param name="request">建立投資組合的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回已建立的投資組合詳細資料；
    /// 若名稱為空白則返回錯誤碼 <c>portfolio.name_required</c>。
    /// </returns>
    Task<Result<PortfolioDetailResponse>> CreateAsync(CreatePortfolioRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 更新指定投資組合的資料。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="request">更新投資組合的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 <c>true</c>；
    /// 若名稱為空白則返回錯誤碼 <c>portfolio.name_required</c>；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>。
    /// </returns>
    Task<Result<bool>> UpdateAsync(Guid id, UpdatePortfolioRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 軟刪除指定投資組合（將其標記為非活躍）。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 <c>true</c>；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>。
    /// </returns>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
