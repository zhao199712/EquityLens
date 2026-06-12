using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;

namespace EquityLens.Api.Services.PortfolioValuations;

/// <summary>
/// 投資組合估值服務介面，提供投資組合市場估值計算功能。
/// </summary>
public interface IPortfolioValuationService
{
    /// <summary>
    /// 計算指定投資組合的當前市場估值，包含各持倉的市值、損益與權重。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回投資組合估值結果；
    /// 若投資組合不存在則返回錯誤碼 <c>portfolio.not_found</c>。
    /// </returns>
    Task<Result<PortfolioValuationResponse>> GetValuationAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// 計算指定投資組合的歷史市場估值序列。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="from">估值起始日期。</param>
    /// <param name="to">估值結束日期。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>成功時返回歷史估值序列；若投資組合不存在則返回錯誤。</returns>
    Task<Result<PortfolioValuationHistoryResponse>> GetValuationHistoryAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
