using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>以原始收盤價重建全歷史還原價的結果；有任何問題時不套用，保留既有數值。</summary>
public sealed record OfficialAdjustedCloseCalculation(
    bool Applied,
    IReadOnlyDictionary<DateOnly, decimal> AdjustedCloseByDate,
    IReadOnlyList<string> Issues,
    int EventCount,
    int NonTradingDays = 0);

/// <summary>
/// 以官方公司行動參考價計算含分割、配股、配息與減資調整的還原收盤價。
/// 每個事件使「事件日之前」的因子乘上 參考價 ÷ 前一日收盤價；最後一個交易日的還原價等於收盤價。
/// </summary>
public static class OfficialAdjustedCloseCalculator
{
    // 事件前收盤價核對：官方資料兩位小數，容許一個最小跳動或 0.1% 的進位差。
    private const decimal BeforePriceAbsoluteTolerance = 0.011m;
    private const decimal BeforePriceRelativeTolerance = 0.001m;
    // 漲跌幅限制外再保留 0.5% 緩衝，避免限制邊界的進位誤判。
    private const decimal LimitBuffer = 0.005m;
    private static readonly DateOnly TenPercentLimitStart = new(2015, 6, 1);

    /// <summary>
    /// 計算還原收盤價。
    /// </summary>
    /// <param name="closes">同一證券的日收盤價（任意順序，日期不重複）。</param>
    /// <param name="actions">官方公司行動。</param>
    public static OfficialAdjustedCloseCalculation Calculate(
        IReadOnlyList<(DateOnly Date, decimal Close)> closes,
        IReadOnlyList<CorporateAction> actions)
    {
        var all = closes.OrderBy(x => x.Date).ToList();
        var issues = new List<string>();
        if (all.Select(x => x.Date).Distinct().Count() != all.Count) return new(false, new Dictionary<DateOnly, decimal>(), ["duplicate_dates"], 0);
        // FinMind 在停止交易日回傳全為 0 的列；視為非交易日，不參與計算。
        var rows = all.Where(x => x.Close > 0).ToList();
        if (rows.Count < 2) return new(false, new Dictionary<DateOnly, decimal>(), ["insufficient_prices"], 0);

        // 只有落在資料期間內、且有前一個交易日可核對的事件會影響還原價。
        var factors = new List<(DateOnly Date, decimal Factor)>();
        foreach (var action in actions.Where(a => a.Date > rows[0].Date && a.Date <= rows[^1].Date).OrderBy(a => a.Date))
        {
            var prior = rows.Last(x => x.Date < action.Date);
            var tolerance = Math.Max(BeforePriceAbsoluteTolerance, prior.Close * BeforePriceRelativeTolerance);
            if (Math.Abs(prior.Close - action.BeforePrice) > tolerance)
                issues.Add($"before_price_mismatch:{action.Date:yyyy-MM-dd}:{action.Kind}:{action.BeforePrice}!={prior.Close}");
            if (action.ReferencePrice <= 0)
            {
                issues.Add($"invalid_reference_price:{action.Date:yyyy-MM-dd}:{action.Kind}");
                continue;
            }
            factors.Add((action.Date, action.ReferencePrice / prior.Close));
        }

        // 非事件區間的原始報酬超過漲跌幅限制，代表官方資料缺少公司行動（或價格錯誤），不可套用。
        for (var i = 1; i < rows.Count; i++)
        {
            var (previous, current) = (rows[i - 1], rows[i]);
            if (factors.Any(f => f.Date > previous.Date && f.Date <= current.Date)) continue;
            var limit = (current.Date >= TenPercentLimitStart ? 0.10m : 0.07m) + LimitBuffer;
            var move = current.Close / previous.Close - 1m;
            if (Math.Abs(move) > limit)
                issues.Add($"unexplained_move:{previous.Date:yyyy-MM-dd}->{current.Date:yyyy-MM-dd}:{move:P2}");
        }

        if (issues.Count > 0) return new(false, new Dictionary<DateOnly, decimal>(), issues, factors.Count);

        var adjusted = new Dictionary<DateOnly, decimal>(rows.Count);
        var factor = 1m;
        var next = factors.Count - 1;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            while (next >= 0 && factors[next].Date > rows[i].Date)
            {
                factor *= factors[next].Factor;
                next--;
            }
            adjusted[rows[i].Date] = Math.Round(rows[i].Close * factor, 6, MidpointRounding.AwayFromZero);
        }
        // 非交易日沿用前一個交易日的還原價：沒有成交即沒有價格變動，不推算價格。
        // 最早的有效交易日之前的非交易日沒有依據，維持原值。
        var nonTrading = 0;
        decimal? carried = null;
        foreach (var row in all)
        {
            if (row.Close > 0) { carried = adjusted[row.Date]; continue; }
            if (carried is null) continue;
            adjusted[row.Date] = carried.Value;
            nonTrading++;
        }
        return new(true, adjusted, [], factors.Count, nonTrading);
    }
}
