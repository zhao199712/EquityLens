using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Risk;

using static EquityLens.Api.Services.RiskAnalysis.PortfolioRiskModelCalculator;

namespace EquityLens.Api.Services.RiskAnalysis;

internal static class PortfolioStressScenarioCalculator
{
    internal static IReadOnlyList<PortfolioStressScenarioResponse> Calculate(
        IReadOnlyList<Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings,
        IReadOnlyDictionary<Guid, decimal> weights,
        IReadOnlyDictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>> prices,
        IReadOnlyDictionary<Guid, decimal> latestById)
    {
        var scenarios = new List<PortfolioStressScenarioResponse>();
        foreach (var scenario in new[]
        {
            ("covid", "COVID-19 市場急跌", new DateOnly(2020, 2, 19), new DateOnly(2020, 3, 23)),
            ("rates_2022", "2022 升息熊市", new DateOnly(2022, 1, 3), new DateOnly(2022, 10, 14)),
            ("tech_2024", "2024 科技股修正", new DateOnly(2024, 7, 11), new DateOnly(2024, 8, 5))
        })
            scenarios.Add(BuildHistoricalStress(scenario.Item1, scenario.Item2, scenario.Item3, scenario.Item4, holdings, weights, prices));
        scenarios.Add(BuildWorstTwentyDayStress(holdings, weights, prices));
        scenarios.Add(BuildHypotheticalStress("taiwan_strait", "台海供應鏈中斷", holdings, weights, latestById, -.45m, -.30m, -.35m));
        scenarios.Add(BuildHypotheticalStress("ai_correction", "AI 泡沫修正", holdings, weights, latestById, -.30m, -.15m, -.20m));
        return scenarios;
    }

    private static PortfolioStressScenarioResponse BuildHistoricalStress(string id, string name, DateOnly from, DateOnly to, IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>> prices)
    {
        var details = new List<PortfolioStressHoldingResponse>();
        foreach (var h in holdings)
        {
            var series = prices.GetValueOrDefault(h.SecurityId) ?? Array.Empty<Contracts.MarketPrices.MarketPriceResponse>();
            var start = series.Where(p => DateOnly.FromDateTime(p.PriceTime) >= from).OrderBy(p => p.PriceTime).FirstOrDefault();
            var end = series.Where(p => DateOnly.FromDateTime(p.PriceTime) <= to).OrderByDescending(p => p.PriceTime).FirstOrDefault();
            if (start is null || end is null || start.Close <= 0) return new(id, name, "historical", "insufficient_prices", "共同價格不足，無法回放此歷史情境", from, to, 0, Array.Empty<PortfolioStressHoldingResponse>(), Array.Empty<PortfolioStressIndustryResponse>());
            var shock = end.Close / start.Close - 1;
            details.Add(new(h.Ticker, h.SecurityName, ResolveIndustry(h), weights.GetValueOrDefault(h.SecurityId), start.Close, end.Close, shock, weights.GetValueOrDefault(h.SecurityId) * shock));
        }
        return BuildStressResponse(id, name, "historical", "ready", $"以目前持倉權重回放 {from:yyyy-MM-dd} 至 {to:yyyy-MM-dd} 的價格報酬", from, to, details);
    }

    private static PortfolioStressScenarioResponse BuildWorstTwentyDayStress(IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>> prices)
    {
        var common = prices.Values.Select(series => series.Select(p => DateOnly.FromDateTime(p.PriceTime)).ToHashSet()).Aggregate((left, right) => { left.IntersectWith(right); return left; }).Order().ToList();
        if (common.Count < 21) return new("worst_20d", "資料期間最差 20 日", "historical", "insufficient_prices", "共同價格不足 21 日", null, null, 0, Array.Empty<PortfolioStressHoldingResponse>(), Array.Empty<PortfolioStressIndustryResponse>());
        decimal best = 1m; DateOnly from = common[0], to = common[20];
        for (var i = 20; i < common.Count; i++) { decimal impact = 0; foreach (var h in holdings) { var s = prices[h.SecurityId]; var a = s.First(p => DateOnly.FromDateTime(p.PriceTime) == common[i - 20]).Close; var b = s.First(p => DateOnly.FromDateTime(p.PriceTime) == common[i]).Close; impact += weights.GetValueOrDefault(h.SecurityId) * (b / a - 1); } if (impact < best) { best = impact; from = common[i - 20]; to = common[i]; } }
        return BuildHistoricalStress("worst_20d", "資料期間最差 20 日", from, to, holdings, weights, prices);
    }

    private static PortfolioStressScenarioResponse BuildHypotheticalStress(string id, string name, IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, decimal> latest, decimal semiconductor, decimal finance, decimal other)
    {
        var details = holdings.Select(h => { var industry = ResolveIndustry(h); var shock = industry.Contains("半導體") ? semiconductor : industry.Contains("金融") ? finance : other; var weight = weights.GetValueOrDefault(h.SecurityId); var price = latest.GetValueOrDefault(h.SecurityId); return new PortfolioStressHoldingResponse(h.Ticker, h.SecurityName, industry, weight, price, price * (1 + shock), shock, weight * shock); }).ToList();
        return BuildStressResponse(id, name, "hypothetical", "ready", $"半導體 {semiconductor:P0}；金融保險 {finance:P0}；其他／未分類 {other:P0}。假設情境，非發生機率預測。", null, null, details);
    }

    private static PortfolioStressScenarioResponse BuildStressResponse(string id, string name, string type, string status, string methodology, DateOnly? from, DateOnly? to, IReadOnlyList<PortfolioStressHoldingResponse> holdings)
    {
        var industries = holdings.GroupBy(x => x.Industry).Select(g => { var weight = g.Sum(x => x.Weight); var contribution = g.Sum(x => x.Contribution); return new PortfolioStressIndustryResponse(g.Key, weight, weight == 0 ? 0 : contribution / weight, contribution); }).OrderBy(x => x.Contribution).ToList();
        return new(id, name, type, status, methodology, from, to, holdings.Sum(x => x.Contribution), holdings, industries);
    }
}
