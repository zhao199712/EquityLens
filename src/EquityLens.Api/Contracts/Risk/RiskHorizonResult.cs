namespace EquityLens.Api.Contracts.Risk;

/// <summary>
/// 單一持有期間的風險結果。
/// </summary>
/// <param name="HorizonDays">持有期間天數。</param>
/// <param name="HistoricalVaR">滾動持有期間歷史模擬法 VaR。</param>
/// <param name="HistoricalES">滾動持有期間歷史模擬法 Expected Shortfall。</param>
/// <param name="MonteCarloVaR">GBM Monte Carlo 模擬 VaR。</param>
/// <param name="MonteCarloES">GBM Monte Carlo 模擬 ES。</param>
/// <param name="MonteCarloMeanFinalValue">模擬最終價值的平均值。</param>
/// <param name="MonteCarloMedianFinalValue">模擬最終價值的中位數。</param>
/// <param name="MonteCarloWorstCaseFinalValue">信心水準下的最差情境最終價值。</param>
/// <param name="MonteCarloBestCaseFinalValue">信心水準下的最佳情境最終價值。</param>
public sealed record RiskHorizonResult(
    int HorizonDays,
    decimal HistoricalVaR,
    decimal HistoricalES,
    decimal MonteCarloVaR,
    decimal MonteCarloES,
    decimal MonteCarloMeanFinalValue,
    decimal MonteCarloMedianFinalValue,
    decimal MonteCarloWorstCaseFinalValue,
    decimal MonteCarloBestCaseFinalValue);
