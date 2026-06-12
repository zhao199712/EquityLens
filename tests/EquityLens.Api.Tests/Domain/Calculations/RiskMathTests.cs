namespace EquityLens.Api.Domain.Calculations;

public sealed class RiskMathTests
{
    [Theory]
    [InlineData(110, 100, 0.10)]
    [InlineData(90, 100, -0.10)]
    [InlineData(100, 100, 0)]
    [InlineData(0, 100, -1)]
    public void CalculateReturn_ReturnsRatio(
        decimal endValue, decimal startValue, decimal expected)
    {
        var result = RiskMath.CalculateReturn(endValue, startValue);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateReturn_WhenStartIsZero_ReturnsZero()
    {
        var result = RiskMath.CalculateReturn(100, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculatePortfolioReturn_ReturnsWeightedSum()
    {
        var weights = new decimal[] { 0.6m, 0.4m };
        var returns = new decimal[] { 0.02m, 0.01m };
        var result = RiskMath.CalculatePortfolioReturn(weights, returns);
        Assert.Equal(0.016m, result);
    }

    [Fact]
    public void CalculatePortfolioReturn_EmptyInputs_ReturnsZero()
    {
        var result = RiskMath.CalculatePortfolioReturn(
            Array.Empty<decimal>(), Array.Empty<decimal>());
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateVolatility_ReturnsStandardDeviation()
    {
        var returns = new decimal[] { 0.01m, 0.02m, -0.01m, 0.03m, 0m };
        var result = RiskMath.CalculateVolatility(returns);
        Assert.True(result > 0);
    }

    [Fact]
    public void CalculateVolatility_SingleValue_ReturnsZero()
    {
        var returns = new decimal[] { 0.01m };
        var result = RiskMath.CalculateVolatility(returns);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateVolatility_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateVolatility(null!));
        Assert.Equal(0, RiskMath.CalculateVolatility(Array.Empty<decimal>()));
    }

    [Fact]
    public void CalculateAnnualizedVolatility_ScalesBySqrtTradingDays()
    {
        var result = RiskMath.CalculateAnnualizedVolatility(0.01m);
        Assert.Equal(0.1587m, result, 4);
    }

    [Fact]
    public void CalculateAnnualizedVolatility_ZeroVol_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateAnnualizedVolatility(0));
    }

    [Fact]
    public void CalculateEwmaVolatility_ReturnsPositiveForVolatileReturns()
    {
        var returns = new decimal[] { 0.01m, -0.02m, 0.015m, -0.01m, 0.03m };
        var result = RiskMath.CalculateEwmaVolatility(returns, 0.94m);
        Assert.True(result > 0);
    }

    [Fact]
    public void CalculateEwmaVolatility_ZeroReturns_ReturnsZero()
    {
        var returns = new decimal[] { 0m, 0m, 0m, 0m };
        var result = RiskMath.CalculateEwmaVolatility(returns, 0.94m);
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CalculateEwmaVolatility_InvalidLambda_ReturnsZero(double lambda)
    {
        var returns = new decimal[] { 0.01m, -0.02m, 0.015m };
        var result = RiskMath.CalculateEwmaVolatility(returns, (decimal)lambda);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateRollingLogReturns_ReturnsWindowSums()
    {
        var returns = new decimal[] { 0.01m, 0.02m, -0.01m, 0.03m };
        var result = RiskMath.CalculateRollingLogReturns(returns, 2);
        Assert.Equal(new[] { 0.03m, 0.01m, 0.02m }, result);
    }

    [Theory]
    [InlineData(0.10, 0.02, 0.15, 0.5333)]
    [InlineData(0, 0.02, 0.15, -0.1333)]
    public void CalculateSharpeRatio_ReturnsExcessReturnPerRisk(
        double pr, double rf, double vol, double expected)
    {
        var result = RiskMath.CalculateSharpeRatio(
            (decimal)pr, (decimal)rf, (decimal)vol);
        Assert.Equal((decimal)expected, result, 4);
    }

    [Fact]
    public void CalculateSharpeRatio_ZeroVol_ReturnsZero()
    {
        var result = RiskMath.CalculateSharpeRatio(0.10m, 0.02m, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateMaxDrawdown_ReturnsWorstPeakToTrough()
    {
        var values = new decimal[] { 100, 110, 90, 95, 80, 120 };
        var result = RiskMath.CalculateMaxDrawdown(values);
        Assert.Equal(-30m / 110m, result);
    }

    [Fact]
    public void CalculateMaxDrawdown_AlwaysUp_ReturnsZero()
    {
        var values = new decimal[] { 100, 105, 110, 120 };
        var result = RiskMath.CalculateMaxDrawdown(values);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateMaxDrawdown_NullOrSingle_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateMaxDrawdown(null!));
        Assert.Equal(0, RiskMath.CalculateMaxDrawdown(new[] { 100m }));
    }

    [Fact]
    public void CalculateHistoricalVaR_ReturnsThreshold()
    {
        var returns = new decimal[]
        {
            -0.12m, -0.10m, -0.09m, -0.08m, -0.05m,
            -0.03m, -0.01m, 0m, 0.01m, 0.02m, 0.03m, 0.04m,
            -0.06m, -0.07m, -0.11m, -0.04m, -0.02m, 0.05m, 0.06m, 0.07m
        };
        var result = RiskMath.CalculateHistoricalVaR(returns, 0.95m);
        var sorted = returns.OrderBy(x => x).ToList();
        Assert.Equal(sorted[1], result);
    }

    [Fact]
    public void CalculateHistoricalVaR_Empty_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateHistoricalVaR(null!));
        Assert.Equal(0, RiskMath.CalculateHistoricalVaR(Array.Empty<decimal>()));
    }

    [Fact]
    public void CalculateExpectedShortfall_ReturnsTailAverage()
    {
        var returns = new decimal[]
        {
            -0.12m, -0.10m, -0.08m, -0.05m, -0.03m,
            0m, 0.01m, 0.02m, 0.03m, 0.04m
        };
        var result = RiskMath.CalculateExpectedShortfall(returns, 0.90m);
        Assert.True(result <= -0.06m);
    }

    [Fact]
    public void CalculateExpectedShortfall_Empty_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateExpectedShortfall(null!));
        Assert.Equal(0, RiskMath.CalculateExpectedShortfall(Array.Empty<decimal>()));
    }

    [Fact]
    public void RunMonteCarloSimulation_ReturnsWithinRange()
    {
        var result = RiskMath.RunMonteCarloSimulation(
            100m, 0.08m, 0.25m, 30, 5000);
        Assert.True(result.MeanFinalValue > 0);
        Assert.True(result.MedianFinalValue > 0);
        Assert.True(result.BestCase95Percentile >= result.WorstCase95Percentile);
    }

    [Fact]
    public void RunMonteCarloSimulation_AllPricesPositive()
    {
        var result = RiskMath.RunMonteCarloSimulation(
            100m, 0.08m, 0.30m, 60, 2000);
        Assert.True(result.MeanFinalValue > 0);
        Assert.True(result.WorstCase95Percentile > 0);
    }

    [Fact]
    public void RunMonteCarloSimulation_ZeroVol_Deterministic()
    {
        var result = RiskMath.RunMonteCarloSimulation(
            100m, 0.10m, 0m, 252, 1000);
        var expected = 100m * (decimal)Math.Exp(0.10);
        Assert.Equal(expected, result.MeanFinalValue, 1);
    }

    [Fact]
    public void RunMonteCarloSimulation_ZeroSimulations_ReturnsInitial()
    {
        var result = RiskMath.RunMonteCarloSimulation(100m, 0.10m, 0.20m, 30, 0);
        Assert.Equal(100m, result.MeanFinalValue);
    }

    [Fact]
    public void RunMonteCarloSimulation_ZeroDays_ReturnsInitial()
    {
        var result = RiskMath.RunMonteCarloSimulation(100m, 0.10m, 0.20m, 0, 1000);
        Assert.Equal(100m, result.MeanFinalValue);
    }

    [Fact]
    public void CalculateCorrelation_Perfect_ReturnsOne()
    {
        var x = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var y = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var result = RiskMath.CalculateCorrelation(x, y);
        Assert.Equal(1m, result);
    }

    [Fact]
    public void CalculateCorrelation_Negative_ReturnsNegativeOne()
    {
        var x = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var y = new decimal[] { -0.01m, -0.02m, -0.03m, -0.04m, -0.05m };
        var result = RiskMath.CalculateCorrelation(x, y);
        Assert.Equal(-1m, result);
    }

    [Fact]
    public void CalculateCorrelation_NullOrMismatch_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateCorrelation(null!, new[] { 0.01m }));
        Assert.Equal(0, RiskMath.CalculateCorrelation(new[] { 0.01m }, new[] { 0.01m, 0.02m }));
    }

    [Fact]
    public void CalculateCovariance_ReturnsExpected()
    {
        var x = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var y = new decimal[] { 0.02m, 0.04m, 0.06m, 0.08m, 0.10m };
        var result = RiskMath.CalculateCovariance(x, y);
        Assert.True(result > 0);
    }

    [Fact]
    public void CalculateCovariance_RandomIndependent_ReturnsNearZero()
    {
        var x = new decimal[] { 0.01m, -0.01m, 0.02m, -0.02m, 0.005m, -0.005m };
        var y = new decimal[] { 0.005m, 0.015m, -0.01m, 0.02m, -0.025m, -0.005m };
        var result = RiskMath.CalculateCovariance(x, y);
        Assert.True(result >= -0.001m && result <= 0.001m);
    }

    [Fact]
    public void CalculateBeta_ReturnsRatioOfCovToVariance()
    {
        var asset = new decimal[] { 0.02m, 0.04m, 0.03m, 0.05m, 0.01m };
        var market = new decimal[] { 0.01m, 0.02m, 0.015m, 0.025m, 0.005m };
        var result = RiskMath.CalculateBeta(asset, market);
        Assert.True(result > 0);
    }

    [Fact]
    public void CalculateBeta_NullOrMismatch_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculateBeta(null!, new[] { 0.01m }));
        Assert.Equal(0, RiskMath.CalculateBeta(new[] { 0.01m }, Array.Empty<decimal>()));
    }

    [Fact]
    public void CalculatePortfolioVariance_TwoAssets_ReturnsCorrect()
    {
        var weights = new decimal[] { 0.5m, 0.5m };
        var cov = new[]
        {
            new decimal[] { 0.04m, 0.01m },
            new decimal[] { 0.01m, 0.09m }
        };
        var result = RiskMath.CalculatePortfolioVariance(weights, cov);
        Assert.Equal(0.0375m, result);
    }

    [Fact]
    public void CalculatePortfolioVariance_EmptyInputs_ReturnsZero()
    {
        Assert.Equal(0, RiskMath.CalculatePortfolioVariance(
            Array.Empty<decimal>(), Array.Empty<decimal[]>()));
        Assert.Equal(0, RiskMath.CalculatePortfolioVariance(
            new[] { 0.5m }, new[] { new[] { 0.04m, 0.01m } }));
    }

    [Fact]
    public void CalculatePortfolioVolatility_ReturnsSqrtOfVariance()
    {
        var weights = new decimal[] { 0.5m, 0.5m };
        var cov = new[]
        {
            new decimal[] { 0.04m, 0.01m },
            new decimal[] { 0.01m, 0.09m }
        };
        var result = RiskMath.CalculatePortfolioVolatility(weights, cov);
        Assert.Equal((decimal)Math.Sqrt(0.0375), result, 4);
    }

    [Fact]
    public void EstimateGbmParameters_ConstantPrice_ZeroDriftZeroVol()
    {
        var prices = new decimal[] { 100m, 100m, 100m, 100m, 100m };
        var result = RiskMath.EstimateGbmParameters(prices, 252);
        Assert.NotNull(result);
        Assert.Equal(0, result!.AnnualizedDrift);
        Assert.Equal(0, result.AnnualizedVolatility);
        Assert.Equal(5, result.PriceCount);
    }

    [Fact]
    public void EstimateGbmParameters_UpwardTrend_ReturnsPositiveDrift()
    {
        var prices = new decimal[]
        {
            100m, 101m, 102m, 103m, 104m,
            105m, 106m, 107m, 108m, 109m, 110m
        };
        var result = RiskMath.EstimateGbmParameters(prices, 252);
        Assert.NotNull(result);
        Assert.True(result!.AnnualizedDrift > 0);
        Assert.True(result.AnnualizedVolatility > 0);
    }

    [Fact]
    public void EstimateGbmParameters_NullOrInsufficient_ReturnsNull()
    {
        Assert.Null(RiskMath.EstimateGbmParameters(null!));
        Assert.Null(RiskMath.EstimateGbmParameters(Array.Empty<decimal>()));
        Assert.Null(RiskMath.EstimateGbmParameters(new[] { 100m }));
    }

    [Fact]
    public void EstimateGbmParameters_NonPositivePrice_ReturnsNull()
    {
        var prices = new decimal[] { 100m, 0m, 101m };
        Assert.Null(RiskMath.EstimateGbmParameters(prices));

        var negative = new decimal[] { 100m, -5m, 101m };
        Assert.Null(RiskMath.EstimateGbmParameters(negative));
    }

    [Fact]
    public void EstimateGbmParameters_VolatilityScalesWithSqrtTradingDays()
    {
        var prices = new decimal[]
        {
            100m, 102m, 98m, 103m, 97m,
            105m, 95m, 108m, 93m, 110m
        };
        var result252 = RiskMath.EstimateGbmParameters(prices, 252);
        var result100 = RiskMath.EstimateGbmParameters(prices, 100);
        Assert.NotNull(result252);
        Assert.NotNull(result100);
        var ratio = result252!.AnnualizedVolatility / result100!.AnnualizedVolatility;
        var expected = (decimal)Math.Sqrt(252.0 / 100.0);
        Assert.Equal(expected, ratio, 4);
    }

    [Fact]
    public void EstimateGbmParameters_DriftIncludesItoCorrection()
    {
        var prices = new decimal[]
        {
            100m, 101m, 102m, 103m, 104m,
            105m, 106m, 107m, 108m, 109m, 110m
        };
        var result = RiskMath.EstimateGbmParameters(prices, 252);
        Assert.NotNull(result);

        var logReturns = new List<decimal>();
        for (var i = 1; i < prices.Length; i++)
            logReturns.Add((decimal)Math.Log((double)(prices[i] / prices[i - 1])));
        var meanLogReturn = logReturns.Average();
        var annualizedMeanLogReturn = meanLogReturn * 252;
        var sigma2 = result!.AnnualizedVolatility * result.AnnualizedVolatility;

        Assert.Equal(annualizedMeanLogReturn + 0.5m * sigma2, result.AnnualizedDrift, 6);
    }
    // ── Correlation Matrix Tests ──

    [Fact]
    public void CalculateCorrelationMatrix_PerfectCorrelation_DiagonalOne()
    {
        var returns1 = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var returns2 = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var matrix = new List<IReadOnlyList<decimal>> { returns1, returns2 };
        var result = RiskMath.CalculateCorrelationMatrix(matrix);
        Assert.Equal(2, result.Length);
        Assert.Equal(1m, result[0][0]);
        Assert.Equal(1m, result[1][1]);
        Assert.Equal(1m, result[0][1], 6);
    }

    [Fact]
    public void CalculateCorrelationMatrix_NegativeCorrelation_ReturnsNegative()
    {
        var x = new decimal[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m };
        var y = new decimal[] { -0.01m, -0.02m, -0.03m, -0.04m, -0.05m };
        var matrix = new List<IReadOnlyList<decimal>> { x, y };
        var result = RiskMath.CalculateCorrelationMatrix(matrix);
        Assert.Equal(-1m, result[0][1], 6);
    }

    // ── Cholesky Decomposition Tests ──

    [Fact]
    public void CholeskyDecompose_LowerTriangularTimesTransposeEqualsOriginal()
    {
        var matrix = new[]
        {
            new decimal[] { 1m, 0.5m },
            new decimal[] { 0.5m, 1m },
        };
        var L = RiskMath.CholeskyDecompose(matrix);
        Assert.NotNull(L);

        var product = new decimal[2][];
        for (var i = 0; i < 2; i++)
        {
            product[i] = new decimal[2];
            for (var j = 0; j < 2; j++)
                for (var k = 0; k < 2; k++)
                    product[i][j] += L![i][k] * L![j][k];
        }

        for (var i = 0; i < 2; i++)
            for (var j = 0; j < 2; j++)
                Assert.Equal(matrix[i][j], product[i][j], 6);
    }

    [Fact]
    public void CholeskyDecompose_NonPositiveDefinite_ReturnsNull()
    {
        var matrix = new[]
        {
            new decimal[] { -1m, 0m },
            new decimal[] { 0m, 1m },
        };
        var result = RiskMath.CholeskyDecompose(matrix);
        Assert.Null(result);
    }

    // ── Correlated GBM MC Tests ──

    [Fact]
    public void RunCorrelatedGbmMonteCarloSimulation_ReturnsPositiveValues()
    {
        var initial = new decimal[] { 100m, 200m };
        var drifts = new decimal[] { 0.08m, 0.10m };
        var vols = new decimal[] { 0.20m, 0.25m };
        var weights = new decimal[] { 0.5m, 0.5m };
        var corr = new[]
        {
            new decimal[] { 1m, 0.5m },
            new decimal[] { 0.5m, 1m },
        };

        var result = RiskMath.RunCorrelatedGbmMonteCarloSimulation(
            initial, drifts, vols, weights, corr, 30, 2000);

        Assert.True(result.MeanFinalValue > 0);
        Assert.True(result.MedianFinalValue > 0);
        Assert.True(result.BestCase95Percentile >= result.WorstCase95Percentile);
        Assert.Equal(0.95m, result.ConfidenceLevel);
    }

    [Fact]
    public void RunCorrelatedGbmMonteCarloSimulation_ZeroVol_Deterministic()
    {
        var initial = new decimal[] { 100m, 200m };
        var drifts = new decimal[] { 0.10m, 0.10m };
        var vols = new decimal[] { 0m, 0m };
        var weights = new decimal[] { 0.5m, 0.5m };
        var corr = new[]
        {
            new decimal[] { 1m, 0m },
            new decimal[] { 0m, 1m },
        };

        var result = RiskMath.RunCorrelatedGbmMonteCarloSimulation(
            initial, drifts, vols, weights, corr, 252, 1000);

        var expected = 150m * (decimal)Math.Exp(0.10);
        Assert.Equal(expected, result.MeanFinalValue, 1);
    }
}
