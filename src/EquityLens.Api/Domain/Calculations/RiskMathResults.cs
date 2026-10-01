namespace EquityLens.Api.Domain.Calculations;

/// <summary>投資組合波動率的 component、marginal 與 incremental 風險來源。</summary>
public sealed record VolatilityRiskContribution(
    decimal ComponentVolatility,
    decimal ComponentRiskShare,
    decimal MarginalVolatility,
    decimal IncrementalVolatility)
{
    public static readonly VolatilityRiskContribution Zero = new(0, 0, 0, 0);
}

/// <summary>
/// 蒙地卡羅模擬結果。
/// </summary>
/// <param name="MeanFinalValue">所有模擬路徑最終價格的平均值。</param>
/// <param name="MedianFinalValue">所有模擬路徑最終價格的中位數。</param>
/// <param name="BestCase95Percentile">指定信心水準下的最佳情境最終價格。</param>
/// <param name="WorstCase95Percentile">指定信心水準下的最差情境最終價格。</param>
/// <param name="SimulatedVaR">基於模擬報酬分布，給定信心水準下的 VaR。</param>
/// <param name="SimulatedES">基於模擬報酬分布，給定信心水準下的 ES / CVaR。</param>
/// <param name="ConfidenceLevel">用於計算 VaR/ES 的信心水準（例如 0.95 = 95%）。</param>
public sealed record MonteCarloResult(
    decimal MeanFinalValue,
    decimal MedianFinalValue,
    decimal BestCase95Percentile,
    decimal WorstCase95Percentile,
    decimal SimulatedVaR,
    decimal SimulatedES,
    decimal ConfidenceLevel);

/// <summary>
/// GBM 參數估計結果，包含漂移項與波動率。
/// </summary>
/// <param name="AnnualizedDrift">年化漂移項 μ（Physical measure expected return）。</param>
/// <param name="AnnualizedVolatility">年化波動率 σ。</param>
/// <param name="PriceCount">用於估計的歷史價格筆數。</param>
/// <param name="ReturnCount">計算出的對數報酬率筆數（= PriceCount - 1）。</param>
/// <param name="TradingDays">年交易日數，用於年化計算。</param>
public sealed record GbmParameters(
    decimal AnnualizedDrift,
    decimal AnnualizedVolatility,
    int PriceCount,
    int ReturnCount,
    int TradingDays);

/// <summary>
/// MVEWMA-FHS 模擬結果。
/// </summary>
public sealed record MvewmaFhsResult(
    decimal MeanFinalValue,
    decimal MedianFinalValue,
    decimal BestCase95Percentile,
    decimal WorstCase95Percentile,
    decimal SimulatedVaR,
    decimal SimulatedES,
    decimal ConfidenceLevel,
    decimal ShrinkageAlpha,
    int AssetCount,
    int CommonTradingDays,
    decimal EwmaLambda,
    decimal InputShrinkageAlpha,
    decimal ResidualCapQuantile,
    decimal CappedDrawRate,
    bool DidFallback);

/// <summary>一年期 MVEWMA-FHS 路徑模擬結果。</summary>
public sealed record MvewmaFhsPathResult(
    IReadOnlyList<MvewmaFhsPathBand> Bands,
    IReadOnlyList<IReadOnlyList<decimal>> SamplePaths,
    decimal PositiveReturnProbability,
    decimal ExpectedReturn,
    decimal P5FinalReturn,
    decimal P1FinalReturn,
    decimal ShrinkageAlpha,
    int CommonTradingDays,
    decimal AnnualizedPortfolioVolatility,
    decimal ResidualNormP99,
    decimal MaxResidualNorm,
    decimal P50FinalReturn,
    decimal P95FinalReturn,
    decimal P99FinalReturn,
    decimal ExpectedMedianGap,
    decimal ResidualCapQuantile,
    decimal CappedDrawRate,
    bool DidFallback);

public sealed record MvewmaFhsPathBand(int Day, decimal P1, decimal P5, decimal P50, decimal P95, decimal P99);
