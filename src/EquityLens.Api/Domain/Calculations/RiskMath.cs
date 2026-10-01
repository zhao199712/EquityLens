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
    public static decimal CalculateReturn(decimal endValue, decimal startValue) =>
        RiskStatistics.CalculateReturn(endValue, startValue);

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
        IReadOnlyList<decimal> returns) =>
        RiskStatistics.CalculatePortfolioReturn(weights, returns);

    /// <summary>
    /// 計算持倉集中度，包含 Herfindahl-Hirschman Index（HHI）與最大單一持倉權重。
    /// 權重會先正規化，因此輸入可為市值或未正規化權重。
    /// </summary>
    public static (decimal Hhi, decimal LargestWeight) CalculateConcentration(
        IReadOnlyList<decimal> weights) =>
        RiskStatistics.CalculateConcentration(weights);

    /// <summary>
    /// 計算樣本變異數（Sample Variance）。
    /// </summary>
    /// <param name="values">數值序列，需至少兩個元素。</param>
    /// <returns>樣本變異數 = Σ(x_i - μ)² / (n - 1)。若序列為 null 或長度不足則回傳 0。</returns>
    /// <remarks>
    /// 模型：樣本變異數模型（Sample Variance, Bessel-corrected）。
    /// 使用 n-1 分母（貝塞爾校正），適合以樣本推估母體變異數。
    /// </remarks>
    public static decimal CalculateVariance(IReadOnlyList<decimal> values) =>
        RiskStatistics.CalculateVariance(values);

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
    public static decimal CalculateVolatility(IReadOnlyList<decimal> returns) =>
        RiskStatistics.CalculateVolatility(returns);

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
        int tradingDays = DefaultTradingDays) =>
        RiskStatistics.CalculateAnnualizedVolatility(dailyVolatility, tradingDays);

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
    public static decimal CalculateEwmaVolatility(
        IReadOnlyList<decimal> returns,
        decimal lambda = 0.94m) =>
        RiskStatistics.CalculateEwmaVolatility(returns, lambda);

    /// <summary>
    /// 將每日對數報酬率聚合為滾動 N 日對數報酬率。
    /// </summary>
    /// <param name="returns">每日對數報酬率序列。</param>
    /// <param name="horizonDays">持有期間天數。</param>
    /// <returns>滾動 N 日對數報酬率序列。</returns>
    public static IReadOnlyList<decimal> CalculateRollingLogReturns(
        IReadOnlyList<decimal> returns,
        int horizonDays) =>
        RiskStatistics.CalculateRollingLogReturns(returns, horizonDays);

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
        decimal volatility) =>
        RiskStatistics.CalculateSharpeRatio(portfolioReturn, riskFreeRate, volatility);

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
    public static decimal CalculateMaxDrawdown(IReadOnlyList<decimal> values) =>
        RiskStatistics.CalculateMaxDrawdown(values);

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
    public static decimal CalculateHistoricalVaR(
        IReadOnlyList<decimal> returns,
        decimal confidenceLevel = 0.95m) =>
        RiskStatistics.CalculateHistoricalVaR(returns, confidenceLevel);

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
        decimal confidenceLevel = 0.95m) =>
        RiskStatistics.CalculateExpectedShortfall(returns, confidenceLevel);

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
        int tradingDays = 252,
        int? randomSeed = null) =>
        GbmRiskSimulation.RunMonteCarloSimulation(initialValue, annualizedDrift, annualizedVolatility, days, simulations, confidenceLevel, tradingDays, randomSeed);

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
        IReadOnlyList<decimal> valuesY) =>
        RiskStatistics.CalculateCorrelation(valuesX, valuesY);

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
        IReadOnlyList<decimal> valuesY) =>
        RiskStatistics.CalculateCovariance(valuesX, valuesY);

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
        IReadOnlyList<decimal> marketReturns) =>
        RiskStatistics.CalculateBeta(assetReturns, marketReturns);

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
        decimal[][] covarianceMatrix) =>
        RiskMatrixMath.CalculatePortfolioVariance(weights, covarianceMatrix);

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
        decimal[][] covarianceMatrix) =>
        RiskMatrixMath.CalculatePortfolioVolatility(weights, covarianceMatrix);

    /// <summary>
    /// 以 covariance matrix 分解指定資產群組的年化波動率風險來源。
    /// Component contribution 可加總為投資組合年化波動率；incremental risk
    /// 假設移除的部位轉為現金，不重新分配至其餘資產。
    /// </summary>
    public static VolatilityRiskContribution CalculateVolatilityRiskContribution(
        IReadOnlyList<decimal> weights,
        decimal[][] covarianceMatrix,
        IReadOnlyCollection<int> indices,
        int tradingDays = 252) =>
        RiskMatrixMath.CalculateVolatilityRiskContribution(weights, covarianceMatrix, indices, tradingDays);

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
        int tradingDays = 252) =>
        GbmRiskSimulation.EstimateGbmParameters(prices, tradingDays);

    /// <summary>
    /// 計算相關係數矩陣（N×N Pearson Correlation Matrix）。
    /// </summary>
    /// <param name="returnsMatrix">各資產報酬率序列集合，每個元素為一檔資產的 log return 序列。</param>
    /// <returns>N×N 相關係數矩陣。若輸入為空或長度不足則回傳空陣列。</returns>
    public static decimal[][] CalculateCorrelationMatrix(
        IReadOnlyList<IReadOnlyList<decimal>> returnsMatrix) =>
        RiskMatrixMath.CalculateCorrelationMatrix(returnsMatrix);

    /// <summary>
    /// Cholesky 分解，將正定矩陣分解為下三角矩陣 L（L × Lᵀ = A）。
    /// </summary>
    /// <param name="matrix">正定對稱矩陣。</param>
    /// <returns>下三角矩陣 L，若矩陣非正定則回傳 null。</returns>
    /// <remarks>
    /// 模型：Cholesky Decomposition。
    /// 公式：A = L Lᵀ，其中 L 為下三角矩陣。
    /// 用於 Correlated Monte Carlo Simulation，將獨立常態亂數轉換為具有指定相關性的亂數。
    /// </remarks>
    public static decimal[][]? CholeskyDecompose(decimal[][] matrix) =>
        RiskMatrixMath.CholeskyDecompose(matrix);

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
    public static MonteCarloResult RunCorrelatedGbmMonteCarloSimulation(
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
        int? randomSeed = null) =>
        GbmRiskSimulation.RunCorrelatedGbmMonteCarloSimulation(initialValues, annualizedDrifts, annualizedVolatilities, weights, correlationMatrix, days, simulations, confidenceLevel, initialPortfolioValue, portfolioWeights, tradingDays, randomSeed);

    /// <summary>
    /// 根據資產數量與共同交易日數量，自動決定 shrinkage alpha。
    /// </summary>
    public static decimal DetermineAutoShrinkageAlpha(int assetCount, int commonTradingDays) =>
        FhsRiskSimulation.DetermineAutoShrinkageAlpha(assetCount, commonTradingDays);

    /// <summary>
    /// 執行 Multivariate EWMA Filtered Historical Simulation（MVEWMA-FHS）。
    /// 使用多元 EWMA covariance + diagonal shrinkage + jitter + 歷史向量殘差 bootstrap 模擬投資組合 VaR/ES。
    /// </summary>
    public static MvewmaFhsResult RunMultivariateFhsSimulation(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal> weights,
        decimal initialPortfolioValue,
        int horizonDays,
        int simulations = 10000,
        decimal confidenceLevel = 0.95m,
        decimal lambda = 0.94m,
        decimal shrinkageAlpha = 0.10m,
        int tradingDays = 252,
        decimal residualCapQuantile = 0m,
        int? randomSeed = null) =>
        FhsRiskSimulation.RunMultivariateFhsSimulation(returnMatrix, weights, initialPortfolioValue, horizonDays, simulations, confidenceLevel, lambda, shrinkageAlpha, tradingDays, residualCapQuantile, randomSeed);

    /// <summary>
    /// 執行一次 MVEWMA-FHS 路徑模擬，並從同一個已排序的模擬分布計算多個信心水準。
    /// 這可避免在 VaR 95% 與 99% 同時需要時重複建立 covariance、residuals 與 5,000 條路徑。
    /// </summary>
    public static IReadOnlyList<MvewmaFhsResult> RunMultivariateFhsSimulationForConfidenceLevels(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal> weights,
        decimal initialPortfolioValue,
        int horizonDays,
        int simulations,
        IReadOnlyList<decimal> confidenceLevels,
        decimal lambda = 0.94m,
        decimal shrinkageAlpha = 0.10m,
        int tradingDays = 252,
        decimal residualCapQuantile = 0m,
        int? randomSeed = null) =>
        FhsRiskSimulation.RunMultivariateFhsSimulationForConfidenceLevels(returnMatrix, weights, initialPortfolioValue, horizonDays, simulations, confidenceLevels, lambda, shrinkageAlpha, tradingDays, residualCapQuantile, randomSeed);

    /// <summary>
    /// 執行可重現的 MVEWMA-FHS 路徑模擬，回傳每日投組累積報酬分位數與少量代表路徑。
    /// </summary>
    public static MvewmaFhsPathResult RunMultivariateFhsPathSimulation(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal> weights,
        int horizonDays,
        int simulations,
        int samplePathCount,
        int randomSeed,
        decimal lambda = 0.94m,
        decimal shrinkageAlpha = 0.10m,
        decimal residualCapQuantile = 0m) =>
        FhsRiskSimulation.RunMultivariateFhsPathSimulation(returnMatrix, weights, horizonDays, simulations, samplePathCount, randomSeed, lambda, shrinkageAlpha, residualCapQuantile);

    /// <summary>判定期望值是否受少數高報酬路徑顯著拉高。</summary>
    public static bool HasMaterialRightSkew(decimal expectedMedianGap, decimal threshold = 0.25m) =>
        FhsRiskSimulation.HasMaterialRightSkew(expectedMedianGap, threshold);

    /// <summary>
    /// 由多資產對數報酬序列建立多元 EWMA covariance 矩陣序列。
    /// </summary>
    public static IReadOnlyList<decimal[][]> CalculateMultivariateEwmaCovariances(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        decimal lambda = 0.94m) =>
        RiskMatrixMath.CalculateMultivariateEwmaCovariances(returnMatrix, lambda);

    /// <summary>
    /// 對 covariance 矩陣序列施加 diagonal shrinkage。
    /// </summary>
    public static IReadOnlyList<decimal[][]> ApplyDiagonalShrinkage(
        IReadOnlyList<decimal[][]> covariances,
        decimal alpha) =>
        RiskMatrixMath.ApplyDiagonalShrinkage(covariances, alpha);

    /// <summary>
    /// 對 covariance 矩陣序列加入 jitter 以確保數值穩定性。
    /// </summary>
    public static IReadOnlyList<decimal[][]> AddJitter(
        IReadOnlyList<decimal[][]> covariances) =>
        RiskMatrixMath.AddJitter(covariances);

    /// <summary>
    /// 由報酬序列與對應 covariance 矩陣計算標準化歷史殘差向量。
    /// </summary>
    public static IReadOnlyList<decimal[]> BuildFilteredResidualVectors(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal[][]> covariances) =>
        RiskMatrixMath.BuildFilteredResidualVectors(returnMatrix, covariances);
}
