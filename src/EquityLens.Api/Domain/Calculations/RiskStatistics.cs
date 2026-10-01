namespace EquityLens.Api.Domain.Calculations;

/// <summary>報酬、波動率與基本風險統計純函數。</summary>
internal static class RiskStatistics
{
    private const int DefaultTradingDays = 252;

    /// <summary>
    /// 計算簡單報酬率（Simple Return）。
    /// </summary>
    /// <param name="endValue">期末價值。</param>
    /// <param name="startValue">期初價值。</param>
    /// <returns>報酬率 = (endValue - startValue) / startValue。若期初價值為零則回傳 0。</returns>
    /// <remarks>
    /// 模型：簡單報酬率模型（Simple Return Model）。
    /// 公式：R = (V_end - V_start) / V_start。
    /// 適用於單期報酬計算，例如個股日報酬或投資組合單期績效。
    /// </remarks>
    internal static decimal CalculateReturn(decimal endValue, decimal startValue)
    {
        return startValue == 0 ? 0 : (endValue - startValue) / startValue;
    }

    /// <summary>
    /// 計算投資組合加權報酬率（Portfolio Weighted Return）。
    /// </summary>
    /// <param name="weights">各資產權重序列，需與 returns 長度一致。</param>
    /// <param name="returns">各資產報酬率序列，需與 weights 長度一致。</param>
    /// <returns>組合報酬 = Σ(weight_i × return_i)。若長度不一致或為空則回傳 0。</returns>
    /// <remarks>
    /// 模型：加權平均報酬模型（Weighted Average Return）。
    /// 公式：R_p = Σ(w_i × r_i)。
    /// 假設權重已正規化，Σ(w_i) = 1。
    /// </remarks>
    internal static decimal CalculatePortfolioReturn(
        IReadOnlyList<decimal> weights,
        IReadOnlyList<decimal> returns)
    {
        if (weights.Count != returns.Count || weights.Count == 0)
            return 0;

        var sum = 0m;
        for (var i = 0; i < weights.Count; i++)
            sum += weights[i] * returns[i];
        return sum;
    }

    /// <summary>
    /// 計算持倉集中度，包含 Herfindahl-Hirschman Index（HHI）與最大單一持倉權重。
    /// 權重會先正規化，因此輸入可為市值或未正規化權重。
    /// </summary>
    internal static (decimal Hhi, decimal LargestWeight) CalculateConcentration(
        IReadOnlyList<decimal> weights)
    {
        if (weights is null || weights.Count == 0)
            return (0, 0);

        var positiveWeights = weights.Where(weight => weight > 0).ToList();
        var total = positiveWeights.Sum();
        if (total <= 0)
            return (0, 0);

        var normalized = positiveWeights.Select(weight => weight / total).ToList();
        return (normalized.Sum(weight => weight * weight), normalized.Max());
    }

    /// <summary>
    /// 計算樣本變異數（Sample Variance）。
    /// </summary>
    /// <param name="values">數值序列，需至少兩個元素。</param>
    /// <returns>樣本變異數 = Σ(x_i - μ)² / (n - 1)。若序列為 null 或長度不足則回傳 0。</returns>
    /// <remarks>
    /// 模型：樣本變異數模型（Sample Variance, Bessel-corrected）。
    /// 使用 n-1 分母（貝塞爾校正），適合以樣本推估母體變異數。
    /// </remarks>
    internal static decimal CalculateVariance(IReadOnlyList<decimal> values)
    {
        if (values is null || values.Count < 2)
            return 0;

        var mean = CalculateMean(values);
        var sumSquared = 0m;
        for (var i = 0; i < values.Count; i++)
        {
            var diff = values[i] - mean;
            sumSquared += diff * diff;
        }
        return sumSquared / (values.Count - 1);
    }

    /// <summary>
    /// 計算波動率（Volatility），即報酬率的樣本標準差。
    /// </summary>
    /// <param name="returns">報酬率序列，需至少兩個元素。</param>
    /// <returns>波動率 = √(樣本變異數)。若序列為 null 或長度不足則回傳 0。</returns>
    /// <remarks>
    /// 模型：樣本標準差模型（Sample Standard Deviation）。
    /// 公式：σ_sample = √(Σ(r_i - r̄)² / (n - 1))。
    /// 波動率為金融風險的核心衡量指標，數值越高代表報酬不確定性越大。
    /// </remarks>
    internal static decimal CalculateVolatility(IReadOnlyList<decimal> returns)
    {
        var variance = CalculateVariance(returns);
        return variance <= 0 ? 0 : (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// 將日波動率年化為年化波動率（Annualized Volatility）。
    /// </summary>
    /// <param name="dailyVolatility">日波動率。</param>
    /// <param name="tradingDays">年交易日數，預設 252。</param>
    /// <returns>年化波動率 = dailyVolatility × √(tradingDays)。</returns>
    /// <remarks>
    /// 模型：平方根時間法則（Square-Root-of-Time Rule）。
    /// 公式：σ_annual = σ_daily × √T。
    /// 假設報酬率獨立同分配（i.i.d.），T 為年交易日數。
    /// 美股與台股慣例取 T = 252，加密貨幣可取 T = 365。
    /// </remarks>
    internal static decimal CalculateAnnualizedVolatility(
        decimal dailyVolatility,
        int tradingDays = DefaultTradingDays)
    {
        return dailyVolatility * (decimal)Math.Sqrt(tradingDays);
    }

    /// <summary>
    /// 使用 EWMA（Exponentially Weighted Moving Average）估計條件日波動率。
    /// </summary>
    /// <param name="returns">每日對數報酬率序列。</param>
    /// <param name="lambda">衰減係數，日資料常用 0.94。</param>
    /// <returns>EWMA 日波動率。若資料不足或 lambda 無效則回傳 0。</returns>
    /// <remarks>
    /// 模型：RiskMetrics EWMA volatility model。
    /// 公式：σ_t² = λσ_{t-1}² + (1 - λ)r_{t-1}²。
    /// 初始變異數使用樣本變異數，讓估計在短樣本下較穩定。
    /// </remarks>
    internal static decimal CalculateEwmaVolatility(
        IReadOnlyList<decimal> returns,
        decimal lambda = 0.94m)
    {
        if (returns is null || returns.Count < 2 || lambda <= 0 || lambda >= 1)
            return 0;

        var variance = CalculateVariance(returns);
        if (variance <= 0)
            return 0;

        for (var i = 0; i < returns.Count; i++)
        {
            variance = lambda * variance + (1 - lambda) * returns[i] * returns[i];
        }

        return variance <= 0 ? 0 : (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// 將每日對數報酬率聚合為滾動 N 日對數報酬率。
    /// </summary>
    /// <param name="returns">每日對數報酬率序列。</param>
    /// <param name="horizonDays">持有期間天數。</param>
    /// <returns>滾動 N 日對數報酬率序列。</returns>
    internal static IReadOnlyList<decimal> CalculateRollingLogReturns(
        IReadOnlyList<decimal> returns,
        int horizonDays)
    {
        if (returns is null || horizonDays <= 0 || returns.Count < horizonDays)
            return Array.Empty<decimal>();

        var rolling = new List<decimal>(returns.Count - horizonDays + 1);
        var windowSum = 0m;
        for (var i = 0; i < returns.Count; i++)
        {
            windowSum += returns[i];
            if (i >= horizonDays)
                windowSum -= returns[i - horizonDays];
            if (i >= horizonDays - 1)
                rolling.Add(windowSum);
        }

        return rolling;
    }

    /// <summary>
    /// 計算夏普比率（Sharpe Ratio）。
    /// </summary>
    /// <param name="portfolioReturn">投資組合報酬率。</param>
    /// <param name="riskFreeRate">無風險利率（如國庫券或定存利率）。</param>
    /// <param name="volatility">投資組合波動率。</param>
    /// <returns>夏普比率 = (portfolioReturn - riskFreeRate) / volatility。若波動率為零則回傳 0。</returns>
    /// <remarks>
    /// 模型：風險調整報酬模型（Risk-Adjusted Return, Mean-Variance Framework）。
    /// 公式：Sharpe = (R_p - R_f) / σ_p。
    /// 衡量每承擔一單位總風險所能獲得的超額報酬。
    /// 夏普比率 &gt; 1 為優良，&gt; 2 為非常優秀，&lt; 0 表示報酬低於無風險利率。
    /// </remarks>
    internal static decimal CalculateSharpeRatio(
        decimal portfolioReturn,
        decimal riskFreeRate,
        decimal volatility)
    {
        return volatility == 0 ? 0 : (portfolioReturn - riskFreeRate) / volatility;
    }

    /// <summary>
    /// 計算最大回撤（Maximum Drawdown）。
    /// </summary>
    /// <param name="values">資產價值時間序列，需至少兩個元素。</param>
    /// <returns>最大回撤 = 歷史峰值到後續谷底的最大跌幅比率，為負值或零。</returns>
    /// <remarks>
    /// 模型：歷史路徑最大回撤模型（Historical Maximum Drawdown）。
    /// 定義：MDD = min_t{ (V_t - Peak_t) / Peak_t }，其中 Peak_t = max_{s ≤ t} V_s。
    /// 回撤 0 表示無虧損；-0.2 表示從歷史最高點最多跌了 20%。
    /// 這是衡量投資組合極端虧損風險的重要指標。
    /// </remarks>
    internal static decimal CalculateMaxDrawdown(IReadOnlyList<decimal> values)
    {
        if (values is null || values.Count < 2)
            return 0;

        var peak = values[0];
        var maxDrawdown = 0m;

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] > peak)
            {
                peak = values[i];
                continue;
            }

            var drawdown = peak == 0 ? 0 : (values[i] - peak) / peak;
            if (drawdown < maxDrawdown)
                maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    /// <summary>
    /// 計算歷史模擬法風險值（Historical Simulation VaR）。
    /// </summary>
    /// <param name="returns">歷史報酬率序列。</param>
    /// <param name="confidenceLevel">信心水準，預設 0.95（95% VaR）。</param>
    /// <returns>VaR = 報酬率排序後左尾 nearest-rank 百分位數的值。</returns>
    /// <remarks>
    /// 公式：VaR_α = Percentile({r_i}, 1 - α)。
    /// 不做常態分配假設，直接以歷史報酬排序取尾部百分位數。
    /// 例如 95% VaR 取最差 5% 報酬的臨界值，代表有 95% 信心單日虧損不會超過此數值。
    /// 限制：完全依賴歷史資料，若歷史未涵蓋極端事件則可能低估風險。
    /// 輸入序列長度建議至少 100 筆以確保統計穩定性。
    /// </remarks>
    internal static decimal CalculateHistoricalVaR(
        IReadOnlyList<decimal> returns,
        decimal confidenceLevel = 0.95m)
    {
        if (returns is null || returns.Count == 0)
            return 0;

        var sorted = returns.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling((1m - confidenceLevel) * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        return sorted[index];
    }

    /// <summary>
    /// 計算預期短缺（Expected Shortfall, ES / CVaR）。
    /// </summary>
    /// <param name="returns">歷史報酬率序列。</param>
    /// <param name="confidenceLevel">信心水準，預設 0.95。</param>
    /// <returns>ES = 所有低於或等於 VaR 的報酬率平均值。</returns>
    /// <remarks>
    /// 模型：歷史模擬法 Expected Shortfall 模型（Historical ES / CVaR）。
    /// 公式：ES_α = E[r | r ≤ VaR_α]。
    /// 先計算 VaR，再取尾部報酬的平均值。
    /// ES 比 VaR 更嚴格：VaR 只回答最差門檻，ES 回答落入最差情境時的平均虧損幅度。
    /// ES 滿足次可加性（sub-additivity），為一致性風險指標，監理機關如 Basel III 推薦使用。
    /// </remarks>
    internal static decimal CalculateExpectedShortfall(
        IReadOnlyList<decimal> returns,
        decimal confidenceLevel = 0.95m)
    {
        if (returns is null || returns.Count == 0)
            return 0;

        var var = CalculateHistoricalVaR(returns, confidenceLevel);
        var tail = returns.Where(x => x <= var).ToList();
        if (tail.Count == 0)
            return var;

        return tail.Average();
    }

    /// <summary>
    /// 計算皮爾森相關係數（Pearson Correlation Coefficient）。
    /// </summary>
    /// <param name="valuesX">第一組數值序列。</param>
    /// <param name="valuesY">第二組數值序列，需與 valuesX 長度一致。</param>
    /// <returns>相關係數 ρ(X,Y) = Cov(X,Y) / (σ_X × σ_Y)，取值範圍 [-1, 1]。</returns>
    /// <remarks>
    /// 模型：皮爾森積差相關係數模型（Pearson Product-Moment Correlation）。
    /// 公式：ρ = Σ(x_i - x̄)(y_i - ȳ) / √(Σ(x_i - x̄)² × Σ(y_i - ȳ)²)。
    /// ρ = 1 完全正相關；ρ = -1 完全負相關；ρ = 0 無線性相關。
    /// 用於衡量兩資產報酬的共變程度，為投資組合風險分散分析的核心指標。
    /// 若任一序列變異數為零則回傳 0。
    /// </remarks>
    internal static decimal CalculateCorrelation(
        IReadOnlyList<decimal> valuesX,
        IReadOnlyList<decimal> valuesY)
    {
        if (valuesX is null || valuesY is null || valuesX.Count < 2 || valuesX.Count != valuesY.Count)
            return 0;

        var meanX = CalculateMean(valuesX);
        var meanY = CalculateMean(valuesY);

        var covSum = 0m;
        var varXSum = 0m;
        var varYSum = 0m;

        for (var i = 0; i < valuesX.Count; i++)
        {
            var diffX = valuesX[i] - meanX;
            var diffY = valuesY[i] - meanY;
            covSum += diffX * diffY;
            varXSum += diffX * diffX;
            varYSum += diffY * diffY;
        }

        if (varXSum == 0 || varYSum == 0)
            return 0;

        var correlation = covSum / (decimal)(Math.Sqrt((double)varXSum) * Math.Sqrt((double)varYSum));
        return Math.Clamp(correlation, -1, 1);
    }

    /// <summary>
    /// 計算樣本共變異數（Sample Covariance）。
    /// </summary>
    /// <param name="valuesX">第一組數值序列。</param>
    /// <param name="valuesY">第二組數值序列，需與 valuesX 長度一致。</param>
    /// <returns>樣本共變異數 = Σ(x_i - x̄)(y_i - ȳ) / (n - 1)。</returns>
    /// <remarks>
    /// 模型：樣本共變異數模型（Sample Covariance）。
    /// 公式：Cov(X,Y) = Σ(x_i - x̄)(y_i - ȳ) / (n - 1)。
    /// 正值表示兩變數同向變動，負值表示反向變動。
    /// 共變異數是計算 Beta、Portfolio Variance 相關係數的基礎。
    /// </remarks>
    internal static decimal CalculateCovariance(
        IReadOnlyList<decimal> valuesX,
        IReadOnlyList<decimal> valuesY)
    {
        if (valuesX is null || valuesY is null || valuesX.Count < 2 || valuesX.Count != valuesY.Count)
            return 0;

        var meanX = CalculateMean(valuesX);
        var meanY = CalculateMean(valuesY);

        var sum = 0m;
        for (var i = 0; i < valuesX.Count; i++)
            sum += (valuesX[i] - meanX) * (valuesY[i] - meanY);

        return sum / (valuesX.Count - 1);
    }

    /// <summary>
    /// 計算 Beta 係數（CAPM Beta）。
    /// </summary>
    /// <param name="assetReturns">個別資產報酬率序列。</param>
    /// <param name="marketReturns">市場報酬率序列，需與 assetReturns 長度一致。</param>
    /// <returns>Beta = Cov(R_asset, R_market) / Var(R_market)。</returns>
    /// <remarks>
    /// 模型：CAPM Beta 模型（Capital Asset Pricing Model Beta）。
    /// 公式：β_i = Cov(R_i, R_m) / Var(R_m)。
    /// Beta 衡量資產對市場的敏感度：
    /// β &gt; 1 比市場更波動（高 Beta）；β = 1 與市場同步；
    /// 0 &lt; β &lt; 1 比市場穩定（防禦型）；β &lt; 0 與市場反向。
    /// 若市場變異數為零則回傳 0。
    /// </remarks>
    internal static decimal CalculateBeta(
        IReadOnlyList<decimal> assetReturns,
        IReadOnlyList<decimal> marketReturns)
    {
        if (assetReturns is null || marketReturns is null ||
            assetReturns.Count < 2 || assetReturns.Count != marketReturns.Count)
            return 0;

        var covariance = CalculateCovariance(assetReturns, marketReturns);
        var variance = CalculateVariance(marketReturns);
        return variance == 0 ? 0 : covariance / variance;
    }

    /// <summary>
    /// 計算序列平均值（算術平均數）。
    /// </summary>
    /// <param name="values">數值序列。</param>
    /// <returns>平均值 = Σ x_i / n。若序列為 null 或空則回傳 0。</returns>
    /// <remarks>模型：算術平均數模型（Arithmetic Mean）。為內部輔助函數。</remarks>
    internal static decimal CalculateMean(IReadOnlyList<decimal> values)
    {
        if (values is null || values.Count == 0)
            return 0;

        var sum = 0m;
        for (var i = 0; i < values.Count; i++)
            sum += values[i];
        return sum / values.Count;
    }
}
