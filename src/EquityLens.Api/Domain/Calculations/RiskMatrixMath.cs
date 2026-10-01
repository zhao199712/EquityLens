using static EquityLens.Api.Domain.Calculations.RiskStatistics;

namespace EquityLens.Api.Domain.Calculations;

/// <summary>投組共變異數、矩陣分解與風險來源純函數。</summary>
internal static class RiskMatrixMath
{
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
    internal static decimal CalculatePortfolioVariance(
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
    internal static decimal CalculatePortfolioVolatility(
        IReadOnlyList<decimal> weights,
        decimal[][] covarianceMatrix)
    {
        var variance = CalculatePortfolioVariance(weights, covarianceMatrix);
        return variance <= 0 ? 0 : (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// 以 covariance matrix 分解指定資產群組的年化波動率風險來源。
    /// Component contribution 可加總為投資組合年化波動率；incremental risk
    /// 假設移除的部位轉為現金，不重新分配至其餘資產。
    /// </summary>
    internal static VolatilityRiskContribution CalculateVolatilityRiskContribution(
        IReadOnlyList<decimal> weights,
        decimal[][] covarianceMatrix,
        IReadOnlyCollection<int> indices,
        int tradingDays = 252)
    {
        var n = weights.Count;
        if (n == 0 || indices.Count == 0 || covarianceMatrix.Length != n || tradingDays <= 0)
            return VolatilityRiskContribution.Zero;

        var selected = indices.Where(index => index >= 0 && index < n).Distinct().ToList();
        if (selected.Count == 0 || covarianceMatrix.Any(row => row.Length != n))
            return VolatilityRiskContribution.Zero;

        var dailyVolatility = CalculatePortfolioVolatility(weights, covarianceMatrix);
        if (dailyVolatility <= 0)
            return VolatilityRiskContribution.Zero;

        var covarianceWithPortfolio = new decimal[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                covarianceWithPortfolio[i] += covarianceMatrix[i][j] * weights[j];

        var componentDaily = selected.Sum(index =>
            weights[index] * covarianceWithPortfolio[index] / dailyVolatility);
        var marginalDaily = selected.Sum(index =>
            covarianceWithPortfolio[index] / dailyVolatility);

        var withoutGroup = weights.ToArray();
        foreach (var index in selected)
            withoutGroup[index] = 0;

        var annualizationFactor = (decimal)Math.Sqrt(tradingDays);
        var annualizedVolatility = dailyVolatility * annualizationFactor;
        var remainingAnnualizedVolatility = CalculatePortfolioVolatility(withoutGroup, covarianceMatrix)
            * annualizationFactor;

        return new VolatilityRiskContribution(
            componentDaily * annualizationFactor,
            componentDaily / dailyVolatility,
            marginalDaily * annualizationFactor,
            annualizedVolatility - remainingAnnualizedVolatility);
    }

    /// <summary>
    /// 計算相關係數矩陣（N×N Pearson Correlation Matrix）。
    /// </summary>
    /// <param name="returnsMatrix">各資產報酬率序列集合，每個元素為一檔資產的 log return 序列。</param>
    /// <returns>N×N 相關係數矩陣。若輸入為空或長度不足則回傳空陣列。</returns>
    internal static decimal[][] CalculateCorrelationMatrix(
        IReadOnlyList<IReadOnlyList<decimal>> returnsMatrix)
    {
        var n = returnsMatrix.Count;
        if (n == 0) return Array.Empty<decimal[]>();

        var corr = new decimal[n][];
        for (var i = 0; i < n; i++)
        {
            corr[i] = new decimal[n];
            corr[i][i] = 1m;
            for (var j = 0; j < i; j++)
            {
                var c = CalculateCorrelation(returnsMatrix[i], returnsMatrix[j]);
                corr[i][j] = c;
                corr[j][i] = c;
            }
        }
        return corr;
    }

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
    internal static decimal[][]? CholeskyDecompose(decimal[][] matrix)
    {
        var n = matrix.Length;
        if (n == 0) return null;

        var L = new decimal[n][];
        for (var i = 0; i < n; i++)
        {
            L[i] = new decimal[n];
            for (var j = 0; j <= i; j++)
            {
                var sum = 0m;
                for (var k = 0; k < j; k++)
                    sum += L[i][k] * L[j][k];

                if (i == j)
                {
                    var val = matrix[i][i] - sum;
                    if (val <= 1e-15m) return null;
                    L[i][i] = (decimal)Math.Sqrt((double)val);
                }
                else
                {
                    if (L[j][j] == 0) return null;
                    L[i][j] = (matrix[i][j] - sum) / L[j][j];
                }
            }
        }
        return L;
    }

    /// <summary>
    /// 由多資產對數報酬序列建立多元 EWMA covariance 矩陣序列。
    /// </summary>
    internal static IReadOnlyList<decimal[][]> CalculateMultivariateEwmaCovariances(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        decimal lambda = 0.94m)
    {
        var n = returnMatrix.Count;
        if (n == 0) return Array.Empty<decimal[][]>();

        var t = returnMatrix[0].Count;

        // Initial sample covariance
        var initialCov = new decimal[n][];
        for (var i = 0; i < n; i++)
        {
            initialCov[i] = new decimal[n];
            for (var j = 0; j <= i; j++)
            {
                initialCov[i][j] = CalculateCovariance(returnMatrix[i], returnMatrix[j]);
                if (j < i)
                    initialCov[j][i] = initialCov[i][j];
            }
        }

        var result = new List<decimal[][]>(t);
        var currentCov = initialCov;

        for (var timeIdx = 0; timeIdx < t; timeIdx++)
        {
            var r = new decimal[n];
            for (var i = 0; i < n; i++)
                r[i] = returnMatrix[i][timeIdx];

            var newCov = new decimal[n][];
            for (var i = 0; i < n; i++)
            {
                newCov[i] = new decimal[n];
                for (var j = 0; j < n; j++)
                    newCov[i][j] = lambda * currentCov[i][j] + (1 - lambda) * r[i] * r[j];
            }

            result.Add(newCov);
            currentCov = newCov;
        }

        return result;
    }

    /// <summary>
    /// 對 covariance 矩陣序列施加 diagonal shrinkage。
    /// </summary>
    internal static IReadOnlyList<decimal[][]> ApplyDiagonalShrinkage(
        IReadOnlyList<decimal[][]> covariances,
        decimal alpha)
    {
        if (alpha <= 0) return covariances;

        var result = new List<decimal[][]>(covariances.Count);
        foreach (var cov in covariances)
        {
            var n = cov.Length;
            var shrunk = new decimal[n][];
            for (var i = 0; i < n; i++)
            {
                shrunk[i] = new decimal[n];
                for (var j = 0; j < n; j++)
                {
                    if (i == j)
                        shrunk[i][j] = cov[i][j];
                    else
                        shrunk[i][j] = (1 - alpha) * cov[i][j];
                }
            }
            result.Add(shrunk);
        }
        return result;
    }

    /// <summary>
    /// 對 covariance 矩陣序列加入 jitter 以確保數值穩定性。
    /// </summary>
    internal static IReadOnlyList<decimal[][]> AddJitter(
        IReadOnlyList<decimal[][]> covariances)
    {
        var result = new List<decimal[][]>(covariances.Count);
        foreach (var cov in covariances)
        {
            var n = cov.Length;
            var diagSum = 0m;
            for (var i = 0; i < n; i++)
                diagSum += cov[i][i];
            var epsilon = n > 0 ? Math.Max(1e-8m * diagSum / n, 1e-10m) : 1e-10m;

            var jittered = new decimal[n][];
            for (var i = 0; i < n; i++)
            {
                jittered[i] = new decimal[n];
                for (var j = 0; j < n; j++)
                {
                    jittered[i][j] = cov[i][j];
                    if (i == j) jittered[i][j] += epsilon;
                }
            }
            result.Add(jittered);
        }
        return result;
    }

    /// <summary>
    /// 由報酬序列與對應 covariance 矩陣計算標準化歷史殘差向量。
    /// </summary>
    internal static IReadOnlyList<decimal[]> BuildFilteredResidualVectors(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal[][]> covariances)
    {
        var n = returnMatrix.Count;
        var t = returnMatrix[0].Count;
        var result = new List<decimal[]>(t);

        for (var timeIdx = 0; timeIdx < t && timeIdx < covariances.Count; timeIdx++)
        {
            var L = CholeskyDecompose(covariances[timeIdx]);
            if (L is null) continue;

            var r = new decimal[n];
            for (var i = 0; i < n; i++)
                r[i] = returnMatrix[i][timeIdx];

            var z = SolveLowerTriangular(L, r);
            if (z is not null)
                result.Add(z);
        }

        return result;
    }

    private static decimal[]? SolveLowerTriangular(decimal[][] L, decimal[] b)
    {
        var n = L.Length;
        var x = new decimal[n];
        for (var i = 0; i < n; i++)
        {
            if (L[i][i] == 0) return null;
            var sum = 0m;
            for (var j = 0; j < i; j++)
                sum += L[i][j] * x[j];
            x[i] = (b[i] - sum) / L[i][i];
        }
        return x;
    }
}
