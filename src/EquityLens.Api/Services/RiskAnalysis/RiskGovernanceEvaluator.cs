using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

internal static class RiskGovernanceEvaluator
{
    internal static PortfolioRiskGovernanceResponse Evaluate(Guid portfolioId, PortfolioRiskResponse value, decimal cashWeight, DateOnly today)
    {
        var alerts = new List<PortfolioRiskAlertResponse>();
        void Add(string code, decimal current, decimal warning, decimal critical, bool lowerIsWorse, string text)
        {
            var status = lowerIsWorse ? current <= critical ? "critical" : current <= warning ? "warning" : "normal" : current > critical ? "critical" : current > warning ? "warning" : "normal";
            alerts.Add(new(code, status, current, warning, critical, text));
        }
        Add("concentration.largest_holding", value.LargestHoldingWeight, .40m, .50m, false, "降低單一持倉或增加分散配置。");
        Add("concentration.hhi", value.ConcentrationHhi, .25m, .35m, false, "降低集中產業或單一標的曝險。");
        var stale = value.DataAsOfDate is null ? 999m : today.DayNumber - value.DataAsOfDate.Value.DayNumber;
        Add("data.price_age_days", stale, 5m, 10m, false, "請同步最新價格資料後重新計算。");
        var leverage = Math.Max(0, -cashWeight);
        Add("leverage.financing", leverage, .20m, .40m, false, "融資槓桿未納入利率、保證金、追繳與被迫平倉風險。");
        return new(portfolioId, stale > 10 ? "critical" : value.CommonTradingDays < 120 ? "critical" : stale > 5 ? "warning" : "ready", value.DataAsOfDate, value.CommonTradingDays ?? 0, alerts.OrderByDescending(a => a.Status).ToList());
    }
}
