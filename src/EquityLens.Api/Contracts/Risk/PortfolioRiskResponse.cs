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
/// <param name="HistoricalAnnualizedVolatility">投資組合 EWMA 年化波動率。</param>
/// <param name="MaxDrawdown">投資組合最大回撤。</param>
/// <param name="SharpeRatio">投資組合夏普比率。</param>
/// <param name="ConfidenceLevel">風險指標的信心水準。</param>
/// <param name="Simulations">蒙地卡羅模擬路徑數量。</param>
/// <param name="VolatilityMethod">波動率估計方法。</param>
/// <param name="EwmaLambda">EWMA 衰減係數。</param>
/// <param name="DriftAssumption">漂移項假設。</param>
/// <param name="SupportedHorizons">固定輸出的風險期限。</param>
/// <param name="Horizons">各期限的 VaR、ES 與蒙地卡羅結果。</param>
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
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal ConfidenceLevel,
    int Simulations,
    string VolatilityMethod,
    decimal EwmaLambda,
    string DriftAssumption,
    IReadOnlyList<int> SupportedHorizons,
    IReadOnlyList<RiskHorizonResult> Horizons,
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
