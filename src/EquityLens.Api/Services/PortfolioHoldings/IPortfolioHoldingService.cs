using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;

namespace EquityLens.Api.Services.PortfolioHoldings;

/// <summary>
/// 投資組合持倉服務介面，提供持倉的查詢與刪除功能。
/// </summary>
public interface IPortfolioHoldingService
{
    /// <summary>
    /// 取得指定投資組合的所有持倉列表。
    /// </summary>
    Task<Result<IReadOnlyList<PortfolioHoldingResponse>>> ListAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除指定投資組合中某支股票的持倉。
    /// </summary>
    Task<Result<bool>> DeleteBySecurityAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken);
}
