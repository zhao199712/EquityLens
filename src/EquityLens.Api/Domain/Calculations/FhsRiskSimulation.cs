using static EquityLens.Api.Domain.Calculations.RiskStatistics;
using static EquityLens.Api.Domain.Calculations.RiskMatrixMath;

namespace EquityLens.Api.Domain.Calculations;

/// <summary>多元 EWMA FHS 模型準備與模擬純函數。</summary>
internal static class FhsRiskSimulation
{
    /// <summary>
    /// 根據資產數量與共同交易日數量，自動決定 shrinkage alpha。
    /// </summary>
    internal static decimal DetermineAutoShrinkageAlpha(int assetCount, int commonTradingDays)
    {
        if (assetCount <= 0 || commonTradingDays <= 0)
            return 0.15m;

        var p = assetCount * (assetCount + 1) / 2;
        var ratio = (decimal)commonTradingDays / p;

        if (assetCount <= 10 && commonTradingDays >= 500) return 0.03m;
        if (assetCount <= 20 && ratio >= 3m) return 0.05m;
        if (ratio >= 1.5m) return 0.08m;
        if (ratio >= 1.0m) return 0.10m;
        return 0.15m;
    }

    /// <summary>
    /// 執行 Multivariate EWMA Filtered Historical Simulation（MVEWMA-FHS）。
    /// 使用多元 EWMA covariance + diagonal shrinkage + jitter + 歷史向量殘差 bootstrap 模擬投資組合 VaR/ES。
    /// </summary>
    internal static MvewmaFhsResult RunMultivariateFhsSimulation(
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
        int? randomSeed = null)
    {
        return RunMultivariateFhsSimulationForConfidenceLevels(
            returnMatrix, weights, initialPortfolioValue, horizonDays, simulations,
            [confidenceLevel], lambda, shrinkageAlpha, tradingDays, residualCapQuantile, randomSeed)[0];
    }

    /// <summary>
    /// 執行一次 MVEWMA-FHS 路徑模擬，並從同一個已排序的模擬分布計算多個信心水準。
    /// 這可避免在 VaR 95% 與 99% 同時需要時重複建立 covariance、residuals 與 5,000 條路徑。
    /// </summary>
    internal static IReadOnlyList<MvewmaFhsResult> RunMultivariateFhsSimulationForConfidenceLevels(
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
        int? randomSeed = null)
    {
        var n = returnMatrix.Count;
        var levels = confidenceLevels.Count == 0 ? [0.95m] : confidenceLevels;
        MvewmaFhsResult Empty(decimal confidence) => new(0, 0, 0, 0, 0, 0, confidence,
            shrinkageAlpha, n, 0, lambda, shrinkageAlpha, residualCapQuantile, 0, true);

        if (n == 0 || simulations <= 0 || horizonDays <= 0)
            return levels.Select(Empty).ToArray();

        var returnLengths = returnMatrix.Select(r => r.Count).ToList();
        var t = returnLengths.Min();
        if (t < 10) return levels.Select(Empty).ToArray();

        var prepared = PrepareModel(returnMatrix, lambda, shrinkageAlpha, residualCapQuantile);
        if (prepared is null) return levels.Select(Empty).ToArray();
        var L = prepared.Cholesky;
        var residuals = prepared.Residuals;
        var residualCap = prepared.ResidualCap;

        // Step 6: Monte Carlo simulation
        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        var finalValues = new decimal[simulations];
        var basePortfolioValue = initialPortfolioValue;
        var residualCount = residuals.Count;
        var cappedDraws = 0;

        for (var s = 0; s < simulations; s++)
        {
            var cumulative = new double[n];

            for (var d = 0; d < horizonDays; d++)
            {
                var zTau = residuals[random.Next(residualCount)];
                if (residualCapQuantile > 0m && ResidualNorm(zTau) > residualCap)
                {
                    zTau = ScaleResidual(zTau, residualCap);
                    cappedDraws++;
                }

                var rSim = new double[n];
                for (var i = 0; i < n; i++)
                {
                    var sum = 0.0;
                    for (var j = 0; j <= i; j++)
                        sum += (double)L[i][j] * (double)zTau[j];
                    rSim[i] = sum;
                }

                for (var i = 0; i < n; i++)
                    cumulative[i] += rSim[i];
            }

            // Residual weight is cash (negative when the scenario uses financing).
            // Cash has a one-period multiplier of 1, so the return stays anchored at 0%.
            var portfolioReturnMultiplier = 1m - weights.Sum();
            for (var i = 0; i < n; i++)
            {
                var assetMultiplier = (decimal)Math.Exp(cumulative[i]);
                portfolioReturnMultiplier += weights[i] * assetMultiplier;
            }
            finalValues[s] = basePortfolioValue * portfolioReturnMultiplier;
        }

        // Step 7: Compute statistics
        Array.Sort(finalValues);
        var mean = finalValues.Average();
        var median = finalValues[simulations / 2];
        var returnDist = finalValues
            .Select(v => basePortfolioValue == 0 ? 0 : (v - basePortfolioValue) / basePortfolioValue)
            .ToList();
        return levels.Select(confidence =>
        {
            var upperIndex = (int)(confidence * simulations);
            var lowerIndex = (int)((1m - confidence) * simulations);
            var bestCase = finalValues[Math.Min(upperIndex, simulations - 1)];
            var worstCase = finalValues[Math.Max(lowerIndex, 0)];
            var simulatedVaR = CalculateHistoricalVaR(returnDist, confidence);
            var simulatedES = CalculateExpectedShortfall(returnDist, confidence);
            return new MvewmaFhsResult(
                mean, median, bestCase, worstCase, simulatedVaR, simulatedES, confidence,
                shrinkageAlpha, n, t, lambda, shrinkageAlpha, residualCapQuantile,
                (decimal)cappedDraws / (simulations * horizonDays), false);
        }).ToArray();
    }

    /// <summary>
    /// 執行可重現的 MVEWMA-FHS 路徑模擬，回傳每日投組累積報酬分位數與少量代表路徑。
    /// </summary>
    internal static MvewmaFhsPathResult RunMultivariateFhsPathSimulation(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        IReadOnlyList<decimal> weights,
        int horizonDays,
        int simulations,
        int samplePathCount,
        int randomSeed,
        decimal lambda = 0.94m,
        decimal shrinkageAlpha = 0.10m,
        decimal residualCapQuantile = 0m)
    {
        var assetCount = returnMatrix.Count;
        var empty = new MvewmaFhsPathResult(
            Array.Empty<MvewmaFhsPathBand>(), Array.Empty<IReadOnlyList<decimal>>(),
            0, 0, 0, 0, shrinkageAlpha, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
        if (assetCount == 0 || weights.Count != assetCount || horizonDays <= 0 || simulations <= 0)
            return empty;

        var commonTradingDays = returnMatrix.Min(series => series.Count);
        if (commonTradingDays < 10) return empty;

        var prepared = PrepareModel(returnMatrix, lambda, shrinkageAlpha, residualCapQuantile);
        if (prepared is null) return empty;
        var covariances = prepared.Covariances;
        var cholesky = prepared.Cholesky;
        var residuals = prepared.Residuals;
        var residualNorms = prepared.ResidualNorms;
        var residualCap = prepared.ResidualCap;

        var allPaths = new decimal[simulations][];
        var finalReturns = new decimal[simulations];
        var random = new Random(randomSeed);
        var cappedDraws = 0;
        for (var simulation = 0; simulation < simulations; simulation++)
        {
            var assetLogReturns = new double[assetCount];
            var cumulativeReturns = new decimal[horizonDays + 1];
            for (var day = 1; day <= horizonDays; day++)
            {
                var residual = residuals[random.Next(residuals.Count)];
                if (residualCapQuantile > 0m && ResidualNorm(residual) > residualCap)
                {
                    residual = ScaleResidual(residual, residualCap);
                    cappedDraws++;
                }
                for (var asset = 0; asset < assetCount; asset++)
                {
                    var simulatedLogReturn = 0.0;
                    for (var component = 0; component <= asset; component++)
                        simulatedLogReturn += (double)cholesky[asset][component] * (double)residual[component];
                    assetLogReturns[asset] += simulatedLogReturn;
                }

                var multiplier = 0m;
                for (var asset = 0; asset < assetCount; asset++)
                    multiplier += weights[asset] * (decimal)Math.Exp(assetLogReturns[asset]);
                cumulativeReturns[day] = multiplier - 1m;
            }
            allPaths[simulation] = cumulativeReturns;
            finalReturns[simulation] = cumulativeReturns[^1];
        }

        var bands = new List<MvewmaFhsPathBand>(horizonDays + 1);
        for (var day = 0; day <= horizonDays; day++)
        {
            var dayReturns = new decimal[simulations];
            for (var simulation = 0; simulation < simulations; simulation++)
                dayReturns[simulation] = allPaths[simulation][day];
            Array.Sort(dayReturns);
            bands.Add(new MvewmaFhsPathBand(day,
                Quantile(dayReturns, 0.01m), Quantile(dayReturns, 0.05m), Quantile(dayReturns, 0.50m),
                Quantile(dayReturns, 0.95m), Quantile(dayReturns, 0.99m)));
        }

        Array.Sort(finalReturns);
        var sampleCount = Math.Min(Math.Max(samplePathCount, 0), simulations);
        var samples = Enumerable.Range(0, sampleCount)
            .Select(index => (IReadOnlyList<decimal>)allPaths[(int)Math.Floor((decimal)index * simulations / sampleCount)])
            .ToList();
        var probabilityPositive = finalReturns.Count(value => value > 0) / (decimal)simulations;
        var p50 = Quantile(finalReturns, 0.50m);
        var p95 = Quantile(finalReturns, 0.95m);
        var p99 = Quantile(finalReturns, 0.99m);
        var annualizedVolatility = returnMatrix.All(series => series.All(value => value == 0m))
            ? 0m
            : CalculateAnnualizedVolatility(CalculatePortfolioVolatility(weights, covariances[^1]));
        return new MvewmaFhsPathResult(bands, samples, probabilityPositive, finalReturns.Average(),
            Quantile(finalReturns, 0.05m), Quantile(finalReturns, 0.01m), shrinkageAlpha,
            commonTradingDays, annualizedVolatility, Quantile(residualNorms, 0.99m), residualNorms[^1],
            p50, p95, p99, finalReturns.Average() - p50, residualCapQuantile,
            (decimal)cappedDraws / (simulations * horizonDays), false);
    }

    private static decimal Quantile(IReadOnlyList<decimal> values, decimal probability)
    {
        if (values.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(probability * values.Count) - 1, 0, values.Count - 1);
        return values[index];
    }

    private static decimal ResidualNorm(IReadOnlyList<decimal> residual) =>
        (decimal)Math.Sqrt(residual.Sum(value => (double)value * (double)value));

    private static decimal[] ScaleResidual(IReadOnlyList<decimal> residual, decimal targetNorm)
    {
        var norm = ResidualNorm(residual);
        if (norm <= 0 || targetNorm >= norm) return residual.ToArray();
        var scale = targetNorm / norm;
        return residual.Select(value => value * scale).ToArray();
    }

    /// <summary>判定期望值是否受少數高報酬路徑顯著拉高。</summary>
    internal static bool HasMaterialRightSkew(decimal expectedMedianGap, decimal threshold = 0.25m) =>
        expectedMedianGap >= threshold;

    private static PreparedFhsModel? PrepareModel(
        IReadOnlyList<IReadOnlyList<decimal>> returnMatrix,
        decimal lambda,
        decimal shrinkageAlpha,
        decimal residualCapQuantile)
    {
        var covariances = AddJitter(ApplyDiagonalShrinkage(
            CalculateMultivariateEwmaCovariances(returnMatrix, lambda), shrinkageAlpha));
        if (covariances.Count == 0) return null;
        var cholesky = CholeskyDecompose(covariances[^1]);
        if (cholesky is null) return null;
        var residuals = BuildFilteredResidualVectors(returnMatrix, covariances);
        if (residuals.Count < 10) return null;
        var residualNorms = residuals.Select(ResidualNorm).OrderBy(value => value).ToList();
        var residualCap = residualCapQuantile > 0m ? Quantile(residualNorms, residualCapQuantile) : 0m;
        return new(covariances, cholesky, residuals, residualNorms, residualCap);
    }

    private sealed record PreparedFhsModel(
        IReadOnlyList<decimal[][]> Covariances,
        decimal[][] Cholesky,
        IReadOnlyList<decimal[]> Residuals,
        IReadOnlyList<decimal> ResidualNorms,
        decimal ResidualCap);

}
