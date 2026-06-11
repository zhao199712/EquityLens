namespace EquityLens.Api.Domain.Calculations;

/// <summary>
/// 投資組合基礎數學函數，提供市值、成本、損益與權重等基礎算術模型。
/// 所有計算均為純函數，無外部依賴，適用於投資組合估值與損益分析。
/// </summary>
public static class PortfolioMath
{
    /// <summary>
    /// 計算持倉市值（Market Value）。
    /// </summary>
    /// <param name="quantity">持有數量。</param>
    /// <param name="latestPrice">最新市場價格。</param>
    /// <returns>市值 = quantity × latestPrice。</returns>
    /// <remarks>模型：基礎乘法算術模型（Market Value = Quantity × Price）。</remarks>
    public static decimal CalculateMarketValue(decimal quantity, decimal latestPrice)
    {
        return quantity * latestPrice;
    }

    /// <summary>
    /// 計算持倉成本價值（Cost Value）。
    /// </summary>
    /// <param name="quantity">持有數量。</param>
    /// <param name="averageCost">平均成本。</param>
    /// <returns>成本價值 = quantity × averageCost。</returns>
    /// <remarks>模型：基礎乘法算術模型（Cost Value = Quantity × Average Cost）。</remarks>
    public static decimal CalculateCostValue(decimal quantity, decimal averageCost)
    {
        return quantity * averageCost;
    }

    /// <summary>
    /// 計算未實現損益（Unrealized PnL）。
    /// </summary>
    /// <param name="marketValue">市值。</param>
    /// <param name="costValue">成本價值。</param>
    /// <returns>未實現損益 = marketValue - costValue。正值為獲利，負值為虧損。</returns>
    /// <remarks>模型：線性差額模型（Unrealized PnL = Market Value - Cost Value）。</remarks>
    public static decimal CalculateUnrealizedPnl(decimal marketValue, decimal costValue)
    {
        return marketValue - costValue;
    }

    /// <summary>
    /// 計算未實現損益比率（Unrealized PnL Percent）。
    /// </summary>
    /// <param name="unrealizedPnl">未實現損益。</param>
    /// <param name="costValue">成本價值。</param>
    /// <returns>報酬率 = unrealizedPnl / costValue。若成本為零則回傳 null。</returns>
    /// <remarks>模型：簡單報酬率模型（Simple Return = (V_end - V_start) / V_start）。</remarks>
    public static decimal? CalculateUnrealizedPnlPercent(decimal unrealizedPnl, decimal costValue)
    {
        return costValue == 0 ? null : unrealizedPnl / costValue;
    }

    /// <summary>
    /// 計算持倉在投資組合中的權重（Weight）。
    /// </summary>
    /// <param name="marketValue">該持倉市值。</param>
    /// <param name="totalMarketValue">投資組合總市值。</param>
    /// <returns>權重 = marketValue / totalMarketValue。若總市值為零則回傳 null。</returns>
    /// <remarks>模型：比例權重模型（Weight_i = MV_i / Σ MV）。用於投資組合配置分析與風險貢獻計算。</remarks>
    public static decimal? CalculateWeight(decimal marketValue, decimal totalMarketValue)
    {
        return totalMarketValue == 0 ? null : marketValue / totalMarketValue;
    }
}
