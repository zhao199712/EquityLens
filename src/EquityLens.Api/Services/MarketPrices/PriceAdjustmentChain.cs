using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>重建完整因子鏈，僅對事件日前價格套用因子。</summary>
public static class PriceAdjustmentChain
{
    public const string Version = "tw-full-series-v1";
    public static IReadOnlyDictionary<DateOnly, decimal> Rebuild(
        IReadOnlyList<ImportedMarketPrice> prices, IReadOnlyList<CorporatePriceAction> actions)
    {
        var factors = new SortedDictionary<DateOnly, decimal>();
        foreach (var group in actions.GroupBy(x => x.Date))
        {
            var official = group.Where(x => x.OfficialFactor.HasValue).ToList();
            decimal factor;
            if (official.Count != 0)
            {
                // 官方合併除權息已涵蓋同日事件，不再乘上配息或配股。
                if (official.Count != 1 || group.Count() != 1)
                    throw new PriceIntegrityException($"Ambiguous combined actions on {group.Key}.");
                factor = official[0].OfficialFactor!.Value;
            }
            else
            {
                factor = 1m;
                var seen = new HashSet<string>();
                foreach (var action in group)
                {
                    if (!seen.Add(action.Kind))
                        throw new PriceIntegrityException($"Duplicate {action.Kind} event on {group.Key}.");
                    if (action.Kind == "cash")
                    {
                        var prior = prices.Where(x => x.Date < action.Date).OrderBy(x => x.Date).LastOrDefault();
                        if (prior is null || prior.Close <= 0 || action.Value < 0 || action.Value >= prior.Close)
                            throw new PriceIntegrityException($"Invalid dividend basis on {action.Date}.");
                        factor *= (prior.Close - action.Value) / prior.Close;
                    }
                    else if (action.Kind == "split" && action.Value > 0)
                        factor /= action.Value;
                    else
                        throw new PriceIntegrityException($"Official factor required for {action.Kind} on {action.Date}.");
                }
            }
            if (factor <= 0) throw new PriceIntegrityException($"Nonpositive factor on {group.Key}.");
            factors.Add(group.Key, factor);
        }

        var result = new Dictionary<DateOnly, decimal>();
        foreach (var price in prices)
        {
            if (price.Close <= 0) throw new PriceIntegrityException($"Nonpositive raw close on {price.Date}.");
            var value = price.Close;
            foreach (var factor in factors.Where(x => price.Date < x.Key))
                value *= factor.Value;
            value = decimal.Round(value, 6, MidpointRounding.ToEven);
            if (value <= 0) throw new PriceIntegrityException($"Adjusted close rounds to zero on {price.Date}.");
            result.Add(price.Date, value);
        }
        return result;
    }
}
