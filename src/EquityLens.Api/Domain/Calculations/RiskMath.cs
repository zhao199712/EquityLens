namespace EquityLens.Api.Domain.Calculations;

/// <summary>
/// 投資組合風險與統計數學函數，提供報酬、波動、風險值、蒙地卡羅模擬等風險模型。
/// 所有計算均為純函數，基於統計學與金融計量模型，無外部依賴。
/// </summary>
public static class RiskMath
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
    public static decimal CalculateReturn(decimal endValue, decimal startValue)
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
    public static decimal CalculatePortfolioReturn(
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
    /// 計算樣本變異數（Sample Variance）。
    /// </summary>
    /// <param name="values">數值序列，需至少兩個元素。</param>
    /// <returns>樣本變異數 = Σ(x_i - μ)² / (n - 1)。若序列為 null 或長度不足則回傳 0。</returns>
    /// <remarks>
    /// 模型：樣本變異數模型（Sample Variance, Bessel-corrected）。
    /// 使用 n-1 分母（貝塞爾校正），適合以樣本推估母體變異數。
    /// </remarks>
    public static decimal CalculateVariance(IReadOnlyList<decimal> values)
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
    public static decimal CalculateVolatility(IReadOnlyList<decimal> returns)
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
    public static decimal CalculateAnnualizedVolatility(
        decimal dailyVolatility,
        int tradingDays = DefaultTradingDays)
    {
        return dailyVolatility * (decimal)Math.Sqrt(tradingDays);
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
    public static decimal CalculateSharpeRatio(
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
    public static decimal CalculateMaxDrawdown(IReadOnlyList<decimal> values)
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
    /// <returns>VaR = 報酬率排序後第 floor((1 - α) × n) 百分位數的值。</returns>
    /// <remarks>
    /// 模型：歷史模擬法 VaR 模型（Historical Simulation Value at Risk）。
    /// 公式：VaR_α = Percentile({r_i}, 1 - α)。
    /// 不做常態分配假設，直接以歷史報酬排序取尾部百分位數。
    /// 例如 95% VaR 取最差 5% 報酬的臨界值，代表有 95% 信心單日虧損不會超過此數值。
    /// 限制：完全依賴歷史資料，若歷史未涵蓋極端事件則可能低估風險。
    /// 輸入序列長度建議至少 100 筆以確保統計穩定性。
    /// </remarks>
    public static decimal CalculateHistoricalVaR(
        IReadOnlyList<decimal> returns,
        decimal confidenceLevel = 0.95m)
    {
        if (returns is null || returns.Count == 0)
            return 0;

        var sorted = returns.OrderBy(x => x).ToList();
        var index = (int)Math.Floor((1m - confidenceLevel) * sorted.Count);
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
    public static decimal CalculateExpectedShortfall(
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
    /// 執行 GBM 蒙地卡羅模擬（Geometric Brownian Motion Monte Carlo Simulation under Physical Measure）。
    /// 以 GBM 模擬未來資產價格路徑，用於投資組合 VaR、ES 與風險情境分析。
    /// </summary>
    /// <param name="initialValue">初始資產價值 S₀。</param>
    /// <param name="annualizedDrift">年化漂移項 μ（Physical measure expected return，非 risk-free rate）。</param>
    /// <param name="annualizedVolatility">年化波動率 σ。</param>
    /// <param name="days">模擬天數（交易日）。</param>
    /// <param name="simulations">模擬路徑數量，預設 10,000。</param>
    /// <param name="confidenceLevel">信心水準，預設 0.95。</param>
    /// <param name="tradingDays">年交易日數，預設 252。</param>
    /// <returns>蒙地卡羅模擬結果，包含平均值、中位數、最佳/最差情境、模擬 VaR 與 ES。</returns>
    /// <remarks>
    /// 模型：幾何布朗運動蒙地卡羅模擬（Geometric Brownian Motion Monte Carlo, Physical Measure）。
    /// <para>
    /// SDE：dS_t = μ S_t dt + σ S_t dW_t
    /// </para>
    /// <para>
    /// 離散化模擬公式（Euler–Maruyama on log-price）：
    /// S_{t+Δt} = S_t × exp((μ - ½σ²)Δt + σ√Δt Z)，Z ~ N(0,1)
    /// </para>
    /// <para>
    /// 其中 Δt = 1 / tradingDays，μ 為年化 drift，σ 為年化波動率。
    /// 常態亂數使用 Box-Muller 轉換產生。
    /// </para>
    /// <para>
    /// Physical measure（P-measure）使用真實世界期望報酬 μ 作為 drift，
    /// 適用於投資組合風險預測、VaR、ES 與 AI 風險摘要。
    /// 若用於衍生品定價，應改用 risk-neutral measure（Q-measure）：
    /// μ → r - q，其中 r 為無風險利率，q 為股利殖利率。
    /// </para>
    /// <para>
    /// 參考文獻：Hull, J.C. (2022) Options, Futures, and Other Derivatives, 11th ed., Chapter 14–15。
    /// </para>
    /// </remarks>
    public static MonteCarloResult RunMonteCarloSimulation(
        decimal initialValue,
        decimal annualizedDrift,
        decimal annualizedVolatility,
        int days,
        int simulations = 10000,
        decimal confidenceLevel = 0.95m,
        int tradingDays = 252)
    {
        if (simulations <= 0 || days <= 0)
            return new MonteCarloResult(initialValue, initialValue, initialValue, initialValue, 0, 0, confidenceLevel);

        var random = new Random();
        var finalValues = new decimal[simulations];
        var dt = 1.0 / tradingDays;
        var sigma = (double)annualizedVolatility;
        var drift = ((double)annualizedDrift - 0.5 * sigma * sigma) * dt;
        var diffusionBase = sigma * Math.Sqrt(dt);

        for (var s = 0; s < simulations; s++)
        {
            var price = (double)initialValue;
            for (var d = 0; d < days; d++)
            {
                var z = NextGaussian(random);
                price *= Math.Exp(drift + diffusionBase * z);
            }
            finalValues[s] = price > 0 ? (decimal)price : 0;
        }

        Array.Sort(finalValues);
        var mean = finalValues.Average();
        var median = finalValues[simulations / 2];
        var upperIndex = (int)(confidenceLevel * simulations);
        var lowerIndex = (int)((1m - confidenceLevel) * simulations);
        var bestCase = finalValues[Math.Min(upperIndex, simulations - 1)];
        var worstCase = finalValues[Math.Max(lowerIndex, 0)];

        var returnDist = finalValues.Select(v => initialValue == 0 ? 0 : (v - initialValue) / initialValue).ToList();
        var simulatedVar95 = CalculateHistoricalVaR(returnDist, confidenceLevel);
        var simulatedEs95 = CalculateExpectedShortfall(returnDist, confidenceLevel);

        return new MonteCarloResult(mean, median, bestCase, worstCase, simulatedVar95, simulatedEs95, confidenceLevel);
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
    public static decimal CalculateCorrelation(
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
    public static decimal CalculateCovariance(
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
    public static decimal CalculateBeta(
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
    /// 計算投資組合變異數（Portfolio Variance），基於權重向量與共變異數矩陣。
    /// </summary>
    /// <param name="weights">各資產權重序列。</param>
    /// <param name="covarianceMatrix">N × N 共變異數矩陣（jagged array 形式）。</param>
    /// <returns>組合變異數 = wᵀ Σ w = Σ_i Σ_j w_i × w_j × Σ_ij。</returns>
    /// <remarks>
    /// 模型：Markowitz 投資組合變異數模型（Markowitz Portfolio Variance）。
    /// 公式：σ²_p = Σ_i Σ_j w_i × w_j × σ_ij。
    /// 其中 σ_ij = Cov(R_i, R_j)，w_i 為資產 i 的權重。
    /// 此為現代投資組合理論的核心公式，用於計算組合總風險與進行最佳化配置。
    /// </remarks>
    public static decimal CalculatePortfolioVariance(
        IReadOnlyList<decimal> weights,
        decimal[][] covarianceMatrix)
    {
        var n = weights.Count;
        if (n == 0 || covarianceMatrix.Length != n)
            return 0;

        var variance = 0m;
        for (var i = 0; i < n; i++)
        {
            if (covarianceMatrix[i].Length != n)
                return 0;

            for (var j = 0; j < n; j++)
                variance += weights[i] * weights[j] * covarianceMatrix[i][j];
        }

        return variance;
    }

    /// <summary>
    /// 計算投資組合波動率（Portfolio Volatility），即組合變異數的平方根。
    /// </summary>
    /// <param name="weights">各資產權重序列。</param>
    /// <param name="covarianceMatrix">N × N 共變異數矩陣。</param>
    /// <returns>組合波動率 = √(wᵀ Σ w)。</returns>
    /// <remarks>
    /// 模型：Markowitz 投資組合波動率模型（Markowitz Portfolio Volatility）。
    /// 公式：σ_p = √(wᵀ Σ w)。
    /// 即組合變異數的平方根，衡量投資組合整體風險。
    /// </remarks>
    public static decimal CalculatePortfolioVolatility(
        IReadOnlyList<decimal> weights,
        decimal[][] covarianceMatrix)
    {
        var variance = CalculatePortfolioVariance(weights, covarianceMatrix);
        return variance <= 0 ? 0 : (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// 從歷史價格序列估計 GBM 參數（漂移項 μ 與波動率 σ）。
    /// 使用對數報酬率方法進行估計，適用於 Physical measure GBM（真實世界機率）。
    /// </summary>
    /// <param name="prices">歷史價格序列（按時間升冪排序），例如 AdjustedClose。</param>
    /// <param name="tradingDays">年交易日數，預設 252。</param>
    /// <returns>GBM 參數估計結果。若價格序列為 null、長度不足 2，或包含非正價格則回傳 null。</returns>
    /// <remarks>
    /// 模型：GBM 參數最大概似估計（MLE for GBM Parameters under Physical Measure）。
    /// <para>
    /// 給定價格序列 {S_0, S_1, ..., S_n}，計算每日對數報酬率：
    /// r_t = ln(S_t / S_{t-1})，for t = 1, ..., n。
    /// </para>
    /// <para>
    /// 日波動率：σ_daily = std({r_t})（樣本標準差，分母 n-1）。
    /// 年化波動率：σ_annual = σ_daily × √TradingDays。
    /// </para>
    /// <para>
    /// 日平均對數報酬率：r̄ = mean({r_t})。
    /// 年化漂移項：μ = r̄ × TradingDays + ½σ_annual²。
    /// </para>
    /// <para>
    /// 加回 ½σ² 的原因是：GBM SDE 中 E[ln(S_t/S_{t-1})] = (μ - ½σ²)Δt，
    /// 因此要從樣本平均對數報酬還原 μ 時，需加上 ½σ² 校正項。
    /// 若使用簡化估計（不建議），可省略此校正，這時 drift 為年化對數報酬均值。
    /// </para>
    /// <para>
    /// 參考文獻：Hull, J.C. (2022) Options, Futures, and Other Derivatives, 11th ed.,
    /// Section 15.4 (Estimating Volatility), Section 14.3 (GBM properties)。
    /// </para>
    /// </remarks>
    public static GbmParameters? EstimateGbmParameters(
        IReadOnlyList<decimal> prices,
        int tradingDays = 252)
    {
        if (prices is null || prices.Count < 2)
            return null;

        for (var i = 0; i < prices.Count; i++)
        {
            if (prices[i] <= 0)
                return null;
        }

        var logReturns = new List<decimal>(prices.Count - 1);
        for (var i = 1; i < prices.Count; i++)
        {
            var logReturn = (decimal)Math.Log((double)(prices[i] / prices[i - 1]));
            logReturns.Add(logReturn);
        }

        if (logReturns.Count < 1)
            return null;

        var meanLogReturn = CalculateMean(logReturns);
        var dailyVolatility = CalculateVolatility(logReturns);

        var annualizedVolatility = dailyVolatility * (decimal)Math.Sqrt(tradingDays);
        var annualizedDrift = meanLogReturn * tradingDays + 0.5m * annualizedVolatility * annualizedVolatility;

        return new GbmParameters(
            annualizedDrift,
            annualizedVolatility,
            prices.Count,
            logReturns.Count,
            tradingDays);
    }

    /// <summary>
    /// 計算序列平均值（算術平均數）。
    /// </summary>
    /// <param name="values">數值序列。</param>
    /// <returns>平均值 = Σ x_i / n。若序列為 null 或空則回傳 0。</returns>
    /// <remarks>模型：算術平均數模型（Arithmetic Mean）。為內部輔助函數。</remarks>
    private static decimal CalculateMean(IReadOnlyList<decimal> values)
    {
        if (values is null || values.Count == 0)
            return 0;

        var sum = 0m;
        for (var i = 0; i < values.Count; i++)
            sum += values[i];
        return sum / values.Count;
    }

    /// <summary>
    /// 使用 Box-Muller 轉換產生標準常態分配亂數（N(0,1)）。
    /// </summary>
    /// <param name="random">亂數產生器實例。</param>
    /// <returns>標準常態分配亂數。</returns>
    /// <remarks>
    /// 模型：Box-Muller Transform。
    /// 公式：Z = √(-2 × ln(U₁)) × cos(2π × U₂)，其中 U₁, U₂ ~ Uniform(0,1)。
    /// 用於蒙地卡羅模擬中生成常態分配日報酬。
    /// </remarks>
    private static double NextGaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
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
