using static EquityLens.Api.Domain.Calculations.RiskStatistics;
using static EquityLens.Api.Domain.Calculations.RiskMatrixMath;

namespace EquityLens.Api.Domain.Calculations;

/// <summary>GBM 參數估計與模擬純函數。</summary>
internal static class GbmRiskSimulation
{
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
    internal static MonteCarloResult RunMonteCarloSimulation(
        decimal initialValue,
        decimal annualizedDrift,
        decimal annualizedVolatility,
        int days,
        int simulations = 10000,
        decimal confidenceLevel = 0.95m,
        int tradingDays = 252,
        int? randomSeed = null)
    {
        if (simulations <= 0 || days <= 0)
            return new MonteCarloResult(initialValue, initialValue, initialValue, initialValue, 0, 0, confidenceLevel);

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
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
    internal static GbmParameters? EstimateGbmParameters(
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
    /// 多資產相關 GBM 蒙地卡羅模擬（Correlated GBM Monte Carlo for Multi-Asset Portfolio）。
    /// 使用 Cholesky 分解保留資產間的相關性，模擬未來投資組合價值分布。
    /// </summary>
    /// <param name="initialValues">各資產初始價格 S₀ 序列。</param>
    /// <param name="annualizedDrifts">各資產年化漂移項 μ 序列。</param>
    /// <param name="annualizedVolatilities">各資產年化波動率 σ 序列。</param>
    /// <param name="weights">各資產在投資組合中的權重序列（應和為 1）。</param>
    /// <param name="correlationMatrix">各資產間 N×N 相關係數矩陣。</param>
    /// <param name="days">模擬天數。</param>
    /// <param name="simulations">模擬路徑數量，預設 10,000。</param>
    /// <param name="confidenceLevel">信心水準，預設 0.95。</param>
    /// <param name="initialPortfolioValue">投資組合初始總市值；提供時會以總市值尺度輸出最終價值。</param>
    /// <param name="portfolioWeights">以市值計算的投資組合權重；提供時用於計算各資產報酬對總市值的貢獻。</param>
    /// <param name="tradingDays">年交易日數，預設 252。</param>
    /// <returns>蒙地卡羅模擬結果。</returns>
    /// <remarks>
    /// 模型：多資產相關 GBM 蒙地卡羅模擬（Correlated Geometric Brownian Motion Monte Carlo）。
    /// <para>
    /// 每檔資產的 SDE：dS_i = μ_i S_i dt + σ_i S_i dW_i
    /// </para>
    /// <para>
    /// 其中 dW_i × dW_j = ρ_ij dt，相關性由 correlation matrix 定義。
    /// 使用 Cholesky 分解 L (L Lᵀ = Σ) 將獨立常態亂數 Z 轉換為相關亂數：
    /// Z_corr = L × Z_indep
    /// </para>
    /// <para>
    /// 每日資產價格更新（對數 Euler–Maruyama）：
    /// S_i,t+Δt = S_i,t × exp((μ_i - ½σ_i²)Δt + σ_i√Δt × Z_corr_i)
    /// </para>
    /// <para>
    /// 每條模擬路徑結束時計算投資組合價值：V_T = Σ(w_i × S_i,T)。
    /// 若提供 initialPortfolioValue 與 portfolioWeights，則計算總市值尺度：
    /// V_T = V_0 × Σ(w_i × S_i,T / S_i,0)。
    /// 最後從所有路徑的 V_T 分布中計算 VaR、ES 與統計量。
    /// </para>
    /// <para>
    /// 若 correlation matrix 非正定，則回傳含 0 的預設結果（呼叫端應視為模擬失敗）。
    /// </para>
    /// </remarks>
    internal static MonteCarloResult RunCorrelatedGbmMonteCarloSimulation(
        IReadOnlyList<decimal> initialValues,
        IReadOnlyList<decimal> annualizedDrifts,
        IReadOnlyList<decimal> annualizedVolatilities,
        IReadOnlyList<decimal> weights,
        decimal[][] correlationMatrix,
        int days,
        int simulations = 10000,
        decimal confidenceLevel = 0.95m,
        decimal? initialPortfolioValue = null,
        IReadOnlyList<decimal>? portfolioWeights = null,
        int tradingDays = 252,
        int? randomSeed = null)
    {
        var n = initialValues.Count;

        if (n == 0 || simulations <= 0 || days <= 0)
            return new MonteCarloResult(0, 0, 0, 0, 0, 0, confidenceLevel);

        var L = CholeskyDecompose(correlationMatrix);
        if (L is null)
            return new MonteCarloResult(0, 0, 0, 0, 0, 0, confidenceLevel);

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        var finalValues = new decimal[simulations];
        var dt = 1.0 / tradingDays;
        var sqrtDt = Math.Sqrt(dt);

        var drifts = new double[n];
        var diffusions = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sigma = (double)annualizedVolatilities[i];
            drifts[i] = ((double)annualizedDrifts[i] - 0.5 * sigma * sigma) * dt;
            diffusions[i] = sigma * sqrtDt;
        }

        for (var s = 0; s < simulations; s++)
        {
            var prices = new double[n];
            for (var i = 0; i < n; i++)
                prices[i] = (double)initialValues[i];

            for (var d = 0; d < days; d++)
            {
                var zIndep = new double[n];
                for (var i = 0; i < n; i++)
                    zIndep[i] = NextGaussian(random);

                var zCorr = new double[n];
                for (var i = 0; i < n; i++)
                {
                    var sum = 0.0;
                    for (var j = 0; j <= i; j++)
                        sum += (double)L[i][j] * zIndep[j];
                    zCorr[i] = sum;
                }

                for (var i = 0; i < n; i++)
                {
                    if (prices[i] <= 0) continue;
                    prices[i] *= Math.Exp(drifts[i] + diffusions[i] * zCorr[i]);
                    if (prices[i] < 0) prices[i] = 0;
                }
            }

            var portfolioScaleWeights = portfolioWeights;
            var portfolioValue = 0m;
            if (initialPortfolioValue is null || portfolioScaleWeights is null || portfolioScaleWeights.Count != n)
            {
                for (var i = 0; i < n; i++)
                    portfolioValue += weights[i] * (decimal)prices[i];
            }
            else
            {
                var portfolioReturnMultiplier = 0m;
                for (var i = 0; i < n; i++)
                {
                    var initialValue = initialValues[i];
                    var assetMultiplier = initialValue == 0 ? 0 : (decimal)prices[i] / initialValue;
                    portfolioReturnMultiplier += portfolioScaleWeights[i] * assetMultiplier;
                }
                portfolioValue = initialPortfolioValue.Value * portfolioReturnMultiplier;
            }
            finalValues[s] = portfolioValue;
        }

        Array.Sort(finalValues);
        var mean = finalValues.Average();
        var median = finalValues[simulations / 2];
        var upperIndex = (int)(confidenceLevel * simulations);
        var lowerIndex = (int)((1m - confidenceLevel) * simulations);
        var bestCase = finalValues[Math.Min(upperIndex, simulations - 1)];
        var worstCase = finalValues[Math.Max(lowerIndex, 0)];

        var basePortfolioValue = initialPortfolioValue ?? 0m;
        if (initialPortfolioValue is null)
        {
            for (var i = 0; i < n; i++)
                basePortfolioValue += weights[i] * initialValues[i];
        }

        var returnDist = finalValues
            .Select(v => basePortfolioValue == 0 ? 0 : (v - basePortfolioValue) / basePortfolioValue)
            .ToList();
        var simulatedVaR = CalculateHistoricalVaR(returnDist, confidenceLevel);
        var simulatedES = CalculateExpectedShortfall(returnDist, confidenceLevel);

        return new MonteCarloResult(mean, median, bestCase, worstCase, simulatedVaR, simulatedES, confidenceLevel);
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
