using System.Globalization;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Services.CurrentUser;

/// <summary>只比較已保存分析的同期間數值；沒有同口徑資料時不補算或借用其他期間。</summary>
internal static class RiskProfileEvidence
{
    public static JsonObject Create(RiskPreferenceProfile profile, JsonNode? results, PortfolioDiagnosisContext period,
        RiskThresholdDefinition? definition = null, PortfolioRiskMathInputs? inputs = null)
    {
        definition ??= RiskProfileThresholds.V1;
        var output = System.Text.Json.JsonSerializer.SerializeToNode(profile, AgentNodeJson.SerializerOptions)!.AsObject();
        output["available"] = true;
        output["thresholdStatus"] = definition.Approved ? "ApprovedProductReference" : "PendingProductApproval";
        output["thresholds"] = definition.Approved
            ? System.Text.Json.JsonSerializer.SerializeToNode(definition.For(profile.Value), AgentNodeJson.SerializerOptions) : null;
        // 查詢視窗可能包含週末或無價日；必須使用同一份已凍結輸入的實際共同交易區間。
        // 不能僅因結果日期落在查詢期間內就接受任意其他分析。
        var expected = inputs is not null && inputs.PortfolioId == period.PortfolioId
            ? period with { From = inputs.From, To = inputs.To } : period;
        var fits = new JsonArray();
        foreach (var (metric, operation) in new[] { ("annualVolatility", "calculate-annualized-volatility"), ("maxDrawdown", "calculate-max-drawdown") })
        {
            var fit = new JsonObject { ["metric"] = metric, ["status"] = "Unavailable", ["reason"] = "RiskAnalysisUnavailable" };
            var result = (results as JsonArray)?.OfType<JsonObject>().LastOrDefault(x => x["operation"]?.ToString() == operation);
            var basis = result?["provenance"]?["measurementBasis"] as JsonObject;
            if (!definition.Approved) fit["reason"] = "ThresholdsPendingApproval";
            else if (basis is not null && basis["from"]?.ToString() == expected.From.ToString("yyyy-MM-dd")
                && basis["to"]?.ToString() == expected.To.ToString("yyyy-MM-dd")
                && basis["observationCount"]?.GetValue<int>() >= 2
                && !string.IsNullOrWhiteSpace(basis["weighting"]?.ToString())
                && !string.IsNullOrWhiteSpace(basis["cashTreatment"]?.ToString())
                && result?["value"] is JsonValue v && v.TryGetValue<decimal>(out var value))
            {
                var limits = definition.For(profile.Value);
                var threshold = metric == "annualVolatility" ? limits.AnnualVolatilityMax : limits.MaxDrawdownFloor;
                fit["value"] = value; fit["threshold"] = threshold; fit["basis"] = basis.DeepClone();
                fit["period"] = new JsonObject { ["from"] = expected.From.ToString("yyyy-MM-dd"), ["to"] = expected.To.ToString("yyyy-MM-dd") };
                fit["status"] = value > threshold ? "Above" : value < threshold ? "Below" : "At";
                fit.Remove("reason");
                if (metric == "annualVolatility") fit["ratio"] = decimal.Round(value / threshold, 2, MidpointRounding.AwayFromZero);
                var name = metric == "annualVolatility" ? "年化波動" : "最大回撤";
                var bound = metric == "annualVolatility" ? "上限" : "下限";
                var direction = value > threshold ? "高於" : value < threshold ? "低於" : "等於";
                fit["statement"] = $"{name} {value.ToString("P2", CultureInfo.InvariantCulture)}，{direction}產品參考{bound} {threshold.ToString("P2", CultureInfo.InvariantCulture)}"
                    + (metric == "annualVolatility" ? $"，為參考上限的 {fit["ratio"]} 倍" : "");
            }
            else if (result is not null) fit["reason"] = "PeriodOrBasisMismatch";
            fits.Add(fit);
        }
        output["profileFit"] = fits;
        return output;
    }
}
