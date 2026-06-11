namespace EquityLens.Api.Contracts.Risk;

/// <summary>
/// 個股風險分析結果。
/// </summary>
/// <param name="SecurityId">證券唯一識別碼。</param>
/// <param name="From">歷史價格起始日期。</param>
/// <param name="To">歷史價格結束日期。</param>
/// <param name="PriceCount">估計使用的歷史價格筆數。</param>
/// <param name="ReturnCount">對數報酬率筆數（= PriceCount - 1）。</param>
/// <param name="AnnualizedDrift">GBM 年化漂移項 μ（Physical measure）。</param>
/// <param name="AnnualizedVolatility">GBM 年化波動率 σ。</param>
/// <param name="HistoricalVaR">歷史模擬法 VaR。</param>
/// <param name="HistoricalES">歷史模擬法 Expected Shortfall。</param>
/// <param name="ConfidenceLevel">風險指標的信心水準。</param>
/// <param name="HorizonDays">模擬天數。</param>
/// <param name="Simulations">蒙地卡羅模擬路徑數量。</param>
/// <param name="MonteCarloVaR">GBM Monte Carlo 模擬 VaR。</param>
/// <param name="MonteCarloES">GBM Monte Carlo 模擬 ES。</param>
/// <param name="MonteCarloMeanFinalValue">模擬最終價值的平均值。</param>
/// <param name="MonteCarloMedianFinalValue">模擬最終價值的中位數。</param>
/// <param name="MonteCarloWorstCaseFinalValue">信心水準下的最差情境最終價值。</param>
/// <param name="MonteCarloBestCaseFinalValue">信心水準下的最佳情境最終價值。</param>
public sealed record SecurityRiskResponse(
    Guid SecurityId,
    DateOnly From,
    DateOnly To,
    int PriceCount,
    int ReturnCount,
    decimal AnnualizedDrift,
    decimal AnnualizedVolatility,
    decimal HistoricalVaR,
    decimal HistoricalES,
    decimal ConfidenceLevel,
    int HorizonDays,
    int Simulations,
    decimal MonteCarloVaR,
    decimal MonteCarloES,
    decimal MonteCarloMeanFinalValue,
    decimal MonteCarloMedianFinalValue,
    decimal MonteCarloWorstCaseFinalValue,
    decimal MonteCarloBestCaseFinalValue);
