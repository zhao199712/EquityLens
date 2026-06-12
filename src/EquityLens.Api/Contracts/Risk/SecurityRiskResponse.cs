namespace EquityLens.Api.Contracts.Risk;

/// <summary>
/// 個股風險分析結果。
/// </summary>
/// <param name="SecurityId">證券唯一識別碼。</param>
/// <param name="From">歷史價格起始日期。</param>
/// <param name="To">歷史價格結束日期。</param>
/// <param name="PriceCount">估計使用的歷史價格筆數。</param>
/// <param name="ReturnCount">對數報酬率筆數（= PriceCount - 1）。</param>
/// <param name="AnnualizedDrift">蒙地卡羅模型使用的年化漂移項 μ。</param>
/// <param name="AnnualizedVolatility">EWMA 年化波動率 σ。</param>
/// <param name="ConfidenceLevel">風險指標的信心水準。</param>
/// <param name="Simulations">蒙地卡羅模擬路徑數量。</param>
/// <param name="VolatilityMethod">波動率估計方法。</param>
/// <param name="EwmaLambda">EWMA 衰減係數。</param>
/// <param name="DriftAssumption">漂移項假設。</param>
/// <param name="SupportedHorizons">固定輸出的風險期限。</param>
/// <param name="Horizons">各期限的 VaR、ES 與蒙地卡羅結果。</param>
public sealed record SecurityRiskResponse(
    Guid SecurityId,
    DateOnly From,
    DateOnly To,
    int PriceCount,
    int ReturnCount,
    decimal AnnualizedDrift,
    decimal AnnualizedVolatility,
    decimal ConfidenceLevel,
    int Simulations,
    string VolatilityMethod,
    decimal EwmaLambda,
    string DriftAssumption,
    IReadOnlyList<int> SupportedHorizons,
    IReadOnlyList<RiskHorizonResult> Horizons);
