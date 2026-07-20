using EquityLens.Api.Contracts.Portfolios;

namespace EquityLens.Api.Services.PortfolioValuations;

public static class PortfolioPerformanceCalculator
{
    public static decimal? CalculateTwr(IReadOnlyList<PortfolioValuationHistoryPoint> points)
    {
        decimal value = 1m; var hasReturn = false;
        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1].TotalAssetValue;
            if (previous <= 0 || points[i].TotalAssetValue < 0) continue;
            value *= (points[i].TotalAssetValue - points[i].ExternalCashFlow) / previous;
            hasReturn = true;
        }
        return hasReturn ? value - 1m : null;
    }

    public static IReadOnlyList<BenchmarkPoint> NormalizeBenchmark(IReadOnlyList<DateOnly> dates, IReadOnlyDictionary<DateOnly, decimal> closes)
    {
        decimal? last = closes.Where(x => x.Key <= dates[0]).OrderBy(x => x.Key).Select(x => (decimal?)x.Value).LastOrDefault(); decimal? start = null; var result = new List<BenchmarkPoint>();
        foreach (var date in dates)
        {
            if (closes.TryGetValue(date, out var close)) last = close;
            if (start is null && last is > 0) start = last;
            result.Add(new BenchmarkPoint(date, last, start is > 0 && last is not null ? last.Value / start.Value * 100m : null));
        }
        return result;
    }

    public static decimal? CalculateBeta(IReadOnlyList<PortfolioValuationHistoryPoint> points, IReadOnlyList<BenchmarkPoint> benchmark)
    {
        var portfolioReturns = new List<decimal>();
        var benchmarkReturns = new List<decimal>();
        var benchmarkByDate = benchmark.ToDictionary(x => x.Date, x => x.NormalizedValue);

        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            if (!benchmarkByDate.TryGetValue(previous.Date, out var previousBenchmark) || !previousBenchmark.HasValue) continue;
            if (!benchmarkByDate.TryGetValue(current.Date, out var currentBenchmark) || !currentBenchmark.HasValue) continue;
            if (previous.TotalAssetValue <= 0) continue;

            var portfolioReturn = (current.TotalAssetValue - current.ExternalCashFlow) / previous.TotalAssetValue - 1m;
            var benchmarkReturn = currentBenchmark.Value / previousBenchmark.Value - 1m;
            portfolioReturns.Add(portfolioReturn);
            benchmarkReturns.Add(benchmarkReturn);
        }

        if (portfolioReturns.Count < 2) return null;
        return Covariance(portfolioReturns, benchmarkReturns) / Variance(benchmarkReturns);
    }

    public static decimal? CalculateJensenAlpha(decimal portfolioReturn, decimal benchmarkReturn, decimal beta, decimal annualRiskFreeRate, int days)
    {
        if (days <= 0) return null;
        var riskFreePeriod = (decimal)Math.Pow((double)(1m + annualRiskFreeRate), (double)days / 365d) - 1m;
        return portfolioReturn - (riskFreePeriod + beta * (benchmarkReturn - riskFreePeriod));
    }

    /// <summary>
    /// 以區間首個正淨值點作為期初流出、區間末淨值作為期末流入,計算年化 XIRR。
    /// 區間起點尚無持倉(或價格缺失)時,前面的零值點會被跳過,而不是直接回傳 null。
    /// </summary>
    public static decimal? CalculatePeriodXirr(IReadOnlyList<PortfolioValuationHistoryPoint> points)
    {
        if (points.Count < 2 || points[^1].TotalAssetValue <= 0) return null;

        var openIndex = -1;
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (points[i].TotalAssetValue > 0) { openIndex = i; break; }
        }
        if (openIndex < 0) return null;

        var external = new List<(DateOnly Date, decimal Amount)> { (points[openIndex].Date, -points[openIndex].TotalAssetValue) };
        foreach (var point in points.Skip(openIndex + 1))
        {
            if (point.ExternalCashFlow != 0) external.Add((point.Date, -point.ExternalCashFlow));
        }
        external.Add((points[^1].Date, points[^1].TotalAssetValue));
        return CalculateXirr(external);
    }

    public static decimal? CalculateXirr(IReadOnlyList<(DateOnly Date, decimal Amount)> external)
    {
        if (!external.Any(x => x.Amount < 0) || !external.Any(x => x.Amount > 0)) return null;

        var origin = external.Min(x => x.Date);
        double Npv(double rate) => external.Sum(x => (double)x.Amount / Math.Pow(1d + rate, (x.Date.DayNumber - origin.DayNumber) / 365d));
        var low = -0.9999d;
        var high = 10d;
        var lowValue = Npv(low);
        var highValue = Npv(high);
        while (lowValue * highValue > 0 && high < 1_000_000d) { high *= 2; highValue = Npv(high); }
        if (lowValue * highValue > 0) return null;
        for (var i = 0; i < 100; i++)
        {
            var middle = (low + high) / 2;
            var middleValue = Npv(middle);
            if (Math.Abs(middleValue) < 0.000001d) return (decimal)middle;
            if (lowValue * middleValue <= 0) { high = middle; highValue = middleValue; }
            else { low = middle; lowValue = middleValue; }
        }
        return (decimal)((low + high) / 2);
    }

    private static decimal Mean(IReadOnlyList<decimal> values)
        => values.Sum() / values.Count;

    private static decimal Variance(IReadOnlyList<decimal> values)
    {
        var mean = Mean(values);
        return values.Sum(x => (x - mean) * (x - mean)) / (values.Count - 1);
    }

    private static decimal Covariance(IReadOnlyList<decimal> x, IReadOnlyList<decimal> y)
    {
        var meanX = Mean(x);
        var meanY = Mean(y);
        decimal sum = 0m;
        for (var i = 0; i < x.Count; i++) sum += (x[i] - meanX) * (y[i] - meanY);
        return sum / (x.Count - 1);
    }
}
