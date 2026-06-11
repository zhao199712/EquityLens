namespace EquityLens.Api.Contracts.Risk;

/// <summary>
/// 投資組合風險分析結果。
/// </summary>
/// <param name="PortfolioId">投資組合唯一識別碼。</param>
/// <param name="From">歷史價格起始日期。</param>
/// <param name="To">歷史價格結束日期。</param>
/// <param name="BaseCurrency">投資組合基準貨幣。</param>
/// <param name="HoldingCount">投資組合中的持倉數量。</param>
/// <param name="PricedHoldingCount">有最新價格的持倉數量。</param>
/// <param name="AlignedReturnCount">所有資產日期對齊後的報酬率筆數。</param>
/// <param name="TotalMarketValue">投資組合總市值（以基準貨幣計價）。</param>
/// <param name="HistoricalAnnualizedVolatility">投資組合歷史年化波動率。</param>
/// <param name="HistoricalVaR">投資組合歷史模擬法 VaR。</param>
/// <param name="HistoricalES">投資組合歷史模擬法 Expected Shortfall。</param>
/// <param name="MaxDrawdown">投資組合最大回撤。</param>
/// <param name="SharpeRatio">投資組合夏普比率。</param>
/// <param name="ConfidenceLevel">風險指標的信心水準。</param>
/// <param name="HorizonDays">蒙地卡羅模擬天數。</param>
/// <param name="Simulations">蒙地卡羅模擬路徑數量。</param>
/// <param name="MonteCarloVaR">Correlated GBM Monte Carlo 模擬 VaR。</param>
/// <param name="MonteCarloES">Correlated GBM Monte Carlo 模擬 ES。</param>
/// <param name="MonteCarloMeanFinalValue">模擬最終價值的平均值。</param>
/// <param name="MonteCarloMedianFinalValue">模擬最終價值的中位數。</param>
/// <param name="MonteCarloWorstCaseFinalValue">信心水準下的最差情境最終價值。</param>
/// <param name="MonteCarloBestCaseFinalValue">信心水準下的最佳情境最終價值。</param>
/// <param name="Holdings">各持倉的風險貢獻摘要。</param>
public sealed record PortfolioRiskResponse(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    string BaseCurrency,
    int HoldingCount,
    int PricedHoldingCount,
    int AlignedReturnCount,
    decimal TotalMarketValue,
    decimal HistoricalAnnualizedVolatility,
    decimal HistoricalVaR,
    decimal HistoricalES,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal ConfidenceLevel,
    int HorizonDays,
    int Simulations,
    decimal MonteCarloVaR,
    decimal MonteCarloES,
    decimal MonteCarloMeanFinalValue,
    decimal MonteCarloMedianFinalValue,
    decimal MonteCarloWorstCaseFinalValue,
    decimal MonteCarloBestCaseFinalValue,
    IReadOnlyList<PortfolioHoldingRiskResponse> Holdings);

/// <summary>
/// 投資組合中單一持倉的風險貢獻摘要。
/// </summary>
/// <param name="SecurityId">證券唯一識別碼。</param>
/// <param name="Ticker">股票代號。</param>
/// <param name="Exchange">交易所。</param>
/// <param name="SecurityName">證券名稱。</param>
/// <param name="Weight">該持倉佔投資組合的權重（基於最新市值）。</param>
/// <param name="AnnualizedVolatility">該持倉的年化波動率。</param>
/// <param name="DataPointCount">該持倉的歷史價格筆數。</param>
public sealed record PortfolioHoldingRiskResponse(
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string SecurityName,
    decimal Weight,
    decimal AnnualizedVolatility,
    int DataPointCount);
