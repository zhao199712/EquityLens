using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;

namespace EquityLens.Api.Tests.Services.PortfolioValuations;

public sealed class PortfolioPerformanceCalculatorTests
{
    [Fact]
    public void CalculateTwr_WeekendStartAndMidPeriodDeposit_ExcludesExternalCashFlow()
    {
        var points = new[]
        {
            Point(new DateOnly(2026, 1, 3), 100m), // Saturday, carried close
            Point(new DateOnly(2026, 1, 5), 110m),
            Point(new DateOnly(2026, 1, 6), 165m, 50m),
            Point(new DateOnly(2026, 1, 7), 181.5m),
        };
        Assert.Equal(0.265m, PortfolioPerformanceCalculator.CalculateTwr(points));
    }

    [Fact]
    public void NormalizeBenchmark_CarriesPreviousTradingValueAcrossWeekend()
    {
        var dates = new[] { new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 5) };
        var result = PortfolioPerformanceCalculator.NormalizeBenchmark(dates, new Dictionary<DateOnly, decimal> { [dates[0]] = 20000m, [dates[3]] = 20200m });
        Assert.Equal(100m, result[1].NormalizedValue);
        Assert.Equal(101m, result[3].NormalizedValue);
    }

    [Fact]
    public void CalculateBeta_PerfectCorrelationWithBenchmark_ReturnsOne()
    {
        var dates = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4) };
        var points = new[] { Point(dates[0], 100m), Point(dates[1], 101m), Point(dates[2], 103m), Point(dates[3], 104m) };
        var benchmark = new[]
        {
            new BenchmarkPoint(dates[0], 10000m, 100m),
            new BenchmarkPoint(dates[1], 10100m, 101m),
            new BenchmarkPoint(dates[2], 10300m, 103m),
            new BenchmarkPoint(dates[3], 10400m, 104m),
        };

        var beta = PortfolioPerformanceCalculator.CalculateBeta(points, benchmark);

        Assert.NotNull(beta);
        Assert.Equal(1m, beta.Value, precision: 6);
    }

    [Fact]
    public void CalculateBeta_HalfMarketSensitivity_ReturnsZeroPointFive()
    {
        var dates = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4) };
        var points = new[] { Point(dates[0], 100m), Point(dates[1], 100.5m), Point(dates[2], 101.5m), Point(dates[3], 102m) };
        var benchmark = new[]
        {
            new BenchmarkPoint(dates[0], 10000m, 100m),
            new BenchmarkPoint(dates[1], 10100m, 101m),
            new BenchmarkPoint(dates[2], 10300m, 103m),
            new BenchmarkPoint(dates[3], 10400m, 104m),
        };

        var beta = PortfolioPerformanceCalculator.CalculateBeta(points, benchmark);

        Assert.NotNull(beta);
        Assert.Equal(0.5m, beta.Value, precision: 2);
    }

    [Fact]
    public void CalculateBeta_InsufficientData_ReturnsNull()
    {
        var dates = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2) };
        var points = dates.Select(d => Point(d, 100m)).ToArray();
        var benchmark = dates.Select(d => new BenchmarkPoint(d, 10000m, 100m)).ToArray();

        var beta = PortfolioPerformanceCalculator.CalculateBeta(points, benchmark);

        Assert.Null(beta);
    }

    [Fact]
    public void CalculateJensenAlpha_BetaOneAndMatchingMarket_ReturnsZero()
    {
        var alpha = PortfolioPerformanceCalculator.CalculateJensenAlpha(0.10m, 0.10m, 1m, 0.02m, 365);

        Assert.NotNull(alpha);
        Assert.Equal(0m, alpha.Value, precision: 6);
    }

    [Fact]
    public void CalculateJensenAlpha_BetaOneWithOutperformance_ReturnsExcess()
    {
        var alpha = PortfolioPerformanceCalculator.CalculateJensenAlpha(0.15m, 0.10m, 1m, 0.02m, 365);

        Assert.NotNull(alpha);
        Assert.Equal(0.05m, alpha.Value, precision: 6);
    }

    [Fact]
    public void CalculateJensenAlpha_BetaHalf_ReturnsCorrectAlpha()
    {
        // R_p = 10%, R_m = 10%, beta = 0.5, R_f = 2%
        // alpha = 0.10 - (0.02 + 0.5 * (0.10 - 0.02)) = 0.10 - 0.06 = 0.04
        var alpha = PortfolioPerformanceCalculator.CalculateJensenAlpha(0.10m, 0.10m, 0.5m, 0.02m, 365);

        Assert.NotNull(alpha);
        Assert.Equal(0.04m, alpha.Value, precision: 6);
    }

    private static PortfolioValuationHistoryPoint Point(DateOnly date, decimal value, decimal external = 0m)
        => new(date, 0, value, 0, null, 0, 0, 0, value, 0, external);
}
