using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;

namespace EquityLens.Api.Services.PortfolioHoldings;

/// <summary>
/// 投資組合持倉服務介面，提供持倉的查詢、建立、更新與刪除功能。
/// </summary>
public interface IPortfolioHoldingService
{
    /// <summary>
    /// 取得指定投資組合的所有持倉列表。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回持倉列表；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>。
    /// </returns>
    Task<Result<IReadOnlyList<PortfolioHoldingResponse>>> ListAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// 在指定投資組合中建立新的持倉。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="request">建立持倉的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回已建立的持倉資料；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>；
    /// 若證券資料無效則返回錯誤碼 <c>security.required_fields</c>；
    /// 若持倉已存在則返回錯誤碼 <c>holding.duplicate</c>。
    /// </returns>
    Task<Result<PortfolioHoldingResponse>> CreateAsync(Guid portfolioId, CreatePortfolioHoldingRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 更新指定投資組合中的持倉資料。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="holdingId">持倉的唯一識別碼。</param>
    /// <param name="request">更新持倉的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 <c>true</c>；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>；
    /// 若持倉不存在則返回錯誤碼 <c>holding.not_found</c>。
    /// </returns>
    Task<Result<bool>> UpdateAsync(Guid portfolioId, Guid holdingId, UpdatePortfolioHoldingRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除指定投資組合中的持倉。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="holdingId">持倉的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 <c>true</c>；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>；
    /// 若持倉不存在則返回錯誤碼 <c>holding.not_found</c>。
    /// </returns>
    Task<Result<bool>> DeleteAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken);
}
