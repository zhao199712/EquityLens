using EquityLens.Api.Contracts.MarketPrices;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>
/// 多資產日價格對齊。停牌或個別缺價日沿用前一有效價格（該日報酬為 0，復牌日反映完整變動），
/// 不以共同日期交集刪除交易日。交易日取持股價格日期的聯集，因此所有持股皆無價格的日期
/// 不會出現，該日前後仍會形成一筆跨日報酬；沿用前值的天數目前不設上限。
/// </summary>
internal static class RiskPriceAlignment
{
    /// <summary>
    /// 在所有資產皆已有價格且尚未結束的共同區間內，以任一資產有價的日期聯集為交易日，
    /// 區間內缺價日沿用前一有效價格。區間外不外推。
    /// </summary>
    /// <param name="pricesBySecurity">各資產原始日價格。</param>
    /// <param name="selector">取用的價格欄位；回傳 null 代表該筆價格不可用。</param>
    internal static AlignedPrices Align(
        IReadOnlyDictionary<Guid, IReadOnlyList<MarketPriceResponse>> pricesBySecurity,
        Func<MarketPriceResponse, decimal?> selector)
    {
        if (pricesBySecurity.Count == 0 || pricesBySecurity.Values.Any(series => series.Count == 0))
            return AlignedPrices.Empty(pricesBySecurity.Keys);

        var bySecurity = pricesBySecurity.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var byDate = new SortedDictionary<DateOnly, MarketPriceResponse>();
                foreach (var price in pair.Value)
                    byDate[DateOnly.FromDateTime(price.PriceTime)] = price;
                return byDate;
            });
        var start = bySecurity.Values.Max(series => series.Keys.First());
        var end = bySecurity.Values.Min(series => series.Keys.Last());
        if (start > end)
            return AlignedPrices.Empty(pricesBySecurity.Keys);

        var dates = bySecurity.Values
            .SelectMany(series => series.Keys)
            .Where(date => date >= start && date <= end)
            .Distinct()
            .Order()
            .ToList();

        var aligned = new Dictionary<Guid, IReadOnlyList<decimal>>();
        var filled = new Dictionary<Guid, int>();
        var hasUnavailable = false;
        foreach (var (securityId, series) in bySecurity)
        {
            var values = new List<decimal>(dates.Count);
            var fillCount = 0;
            // 資產可能在共同起始日前已有價格、但起始日當天缺價；以起始日前最後一個有效價格作為前值。
            decimal? previous = series
                .Where(pair => pair.Key < start)
                .Select(pair => selector(pair.Value))
                .LastOrDefault(value => value is not null);
            foreach (var date in dates)
            {
                if (series.TryGetValue(date, out var row))
                {
                    var value = selector(row);
                    if (value is null)
                    {
                        hasUnavailable = true;
                        value = previous ?? 0m;
                    }
                    previous = value;
                    values.Add(value.Value);
                }
                else if (previous is not null)
                {
                    fillCount++;
                    values.Add(previous.Value);
                }
                else
                {
                    // 起始日前沒有任何可用價格（例如皆為 null）：視為不可用，由呼叫端拒絕。
                    hasUnavailable = true;
                    values.Add(0m);
                }
            }
            aligned[securityId] = values;
            filled[securityId] = fillCount;
        }

        return new AlignedPrices(dates, aligned, filled, hasUnavailable);
    }
}

/// <summary>對齊後的交易日、價格與各資產沿用前值的日數。</summary>
internal sealed record AlignedPrices(
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> Prices,
    IReadOnlyDictionary<Guid, int> CarriedForwardCounts,
    bool HasUnavailablePrice)
{
    internal static AlignedPrices Empty(IEnumerable<Guid> securityIds) => new(
        [],
        securityIds.ToDictionary(id => id, _ => (IReadOnlyList<decimal>)Array.Empty<decimal>()),
        securityIds.ToDictionary(id => id, _ => 0),
        false);
}
