using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>
/// 風險分析服務介面，提供個股與投資組合的風險指標計算。
/// </summary>
public interface IRiskAnalysisService
{
    /// <summary>
    /// 計算指定證券的風險指標，包含 GBM 參數估計、歷史與 MC VaR/ES。
    /// </summary>
    Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
        Guid securityId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        CancellationToken cancellationToken);

    /// <summary>
    /// 計算指定投資組合的歷史風險指標，包含 VaR、ES、波動率、夏普比率與最大回撤。
    /// 使用 Historical Simulation 方法，不假設常態分配。
    /// 同時執行 Correlated GBM Monte Carlo 模擬，產出多資產組合模擬 VaR/ES。
    /// </summary>
    /// <param name="portfolioId">投資組合唯一識別碼。</param>
    /// <param name="from">歷史價格起始日期。</param>
    /// <param name="to">歷史價格結束日期。</param>
    /// <param name="horizonDays">蒙地卡羅模擬天數（預設 30）。</param>
    /// <param name="confidenceLevel">信心水準（例如 0.95 = 95%）。</param>
    /// <param name="simulations">蒙地卡羅模擬路徑數量（預設 10000）。</param>
    /// <param name="providerUserId">認證使用者的 UserId（由 controller 傳入）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回投資組合風險分析結果；
    /// 若投資組合不存在則返回 <c>portfolio.not_found</c>；
    /// 若價格資料不足則返回 <c>risk.insufficient_prices</c>。
    /// </returns>
    Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        Guid providerUserId,
        CancellationToken cancellationToken);
}
