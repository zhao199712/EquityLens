using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>核對同一情境工具內明示的風險大小／改善幅度比較；不宣稱能理解任意投資建議。</summary>
internal static class PortfolioComparisonClaims
{
    private const string MetricPattern = @"年化波動率|年化波動|波動率|波動|VaR|ES|期望短缺|最大回撤|假設最大回撤";
    private static readonly (string Label, Func<ScenarioMetrics, decimal> Value, bool LowerIsBetter)[] Metrics = [
        ("年化波動率", m => m.AnnualizedVolatility, true),
        ("VaR", m => m.HistoricalVaR, false),
        ("期望短缺", m => m.ExpectedShortfall, false),
        ("最大回撤", m => m.HypotheticalMaxDrawdown, false)
    ];

    // 全部以同一 baseline 計算「有利方向的變化」，不能對負 VaR／ES／回撤取絕對值。
    private static decimal Improvement(ScenarioMetrics baseline, ScenarioMetrics result, int metric) =>
        (Metrics[metric].Value(result) - Metrics[metric].Value(baseline)) * (Metrics[metric].LowerIsBetter ? -1 : 1);

    // SignedChange 與 Magnitude 均採原始報酬小數；Description 統一轉為百分點。
    internal sealed record MetricChange(string Metric, decimal SignedChange, decimal Magnitude,
        string Direction, string Assessment, string Description);

    internal static IReadOnlyList<MetricChange> Changes(PortfolioScenarioComparison comparison, PortfolioScenarioResult scenario)
    {
        if (comparison.Status != "Calculated" || scenario.Status != "Calculated"
            || comparison.Baseline?.Metrics is not { } baseline || scenario.Metrics is not { } result) return [];
        return Metrics.Select(metric =>
        {
            var delta = metric.Value(result) - metric.Value(baseline);
            var magnitude = Math.Abs(delta);
            var direction = delta < 0 ? "下降" : delta > 0 ? "上升" : "不變";
            var assessment = delta == 0 ? "持平" : (metric.LowerIsBetter ? delta < 0 : delta > 0) ? "改善" : "惡化";
            var displayed = decimal.Round(magnitude * 100, 2, MidpointRounding.AwayFromZero);
            var description = delta == 0 ? $"{metric.Label}不變（持平）"
                : displayed == 0 ? $"{metric.Label}變化低於顯示精度，未顯示升降幅度"
                : $"{metric.Label}{direction} {displayed.ToString("F2", CultureInfo.InvariantCulture)} 個百分點（{assessment}）";
            return new MetricChange(metric.Label, delta, magnitude, direction, assessment, description);
        }).ToArray();
    }

    internal static IReadOnlyList<string> Statements(PortfolioScenarioComparison comparison)
    {
        if (comparison.Status != "Calculated" || comparison.Baseline?.Metrics is not { } baseline) return [];
        var scenarios = comparison.Scenarios.Where(s => s.Status == "Calculated" && s.Metrics is not null).ToArray();
        var lines = new List<string>();
        for (var i = 0; i < scenarios.Length; i++)
            for (var j = i + 1; j < scenarios.Length; j++)
                for (var m = 0; m < Metrics.Length; m++)
                {
                    var a = Improvement(baseline, scenarios[i].Metrics!, m);
                    var b = Improvement(baseline, scenarios[j].Metrics!, m);
                    var relation = a == b ? "等於" : a > b ? "大於" : "小於";
                    lines.Add($"情境「{scenarios[i].Name}」的{Metrics[m].Label}改善幅度{relation}情境「{scenarios[j].Name}」（均相對同一原組合；負改善表示惡化）。");
                }
        return lines;
    }

    internal static IReadOnlyList<string> Check(string markdown, IReadOnlyList<LoopDataFact> facts)
    {
        if (!facts.Any(f => f.Tool == "simulate_weight_scenario")) return [];
        var issues = new HashSet<string>();
        foreach (var line in markdown.Split('\n'))
        {
            // 引用只作用於當行；不得拿別處碰巧可支持的情境代替目前主張。
            var ids = Regex.Matches(line, @"\[(P\d+)\]").Select(m => m.Groups[1].Value).ToHashSet();
            var cited = facts.Where(f => ids.Contains(f.Id) && f.Tool == "simulate_weight_scenario").ToArray();
            var pattern = $@"(?<metrics>(?:{MetricPattern})(?:\s*[與和及、]\s*(?:{MetricPattern}))*)\s*的?\s*(?<improvement>改善幅度|改善程度)?\s*(?<negation>並非|不是|不)?\s*(?:略|明顯|都|均|皆|更)?\s*(?<direction>大於|小於|高於|低於|等於)\s*(?<target>(?:情境\s*)?「[^」\r\n]+」|[^，,。；;\[\n]+)";
            foreach (Match claim in Regex.Matches(line, pattern, RegexOptions.IgnoreCase))
            {
                // 數值門檻（例如使用者希望波動低於某百分比）不是兩情境優劣判斷。
                if (Regex.IsMatch(claim.Groups["target"].Value, @"^[-+]?\d")) continue;
                var verified = false;
                foreach (var fact in cited)
                {
                    PortfolioScenarioComparison? comparison;
                    try { comparison = fact.Data.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions); }
                    catch (JsonException) { continue; }
                    if (comparison?.Status != "Calculated" || comparison.Baseline?.Metrics is not { } baseline || comparison.Scenarios is null) continue;
                    var candidates = comparison.Scenarios.Where(s => s.Status == "Calculated" && s.Metrics is not null).ToArray();
                    // 主詞限定當句；「其」只能承接當句最後明示的情境，不能跨句猜測。
                    var prefix = line[..claim.Index];
                    prefix = prefix[(prefix.LastIndexOf('。') + 1)..];
                    prefix = Regex.Replace(prefix, pattern, "", RegexOptions.IgnoreCase);
                    var left = Resolve(prefix, candidates, lastMention: true);
                    var right = Resolve(claim.Groups["target"].Value, candidates, lastMention: false);
                    if (left is null || right is null || left == right || claim.Groups["negation"].Success) continue;
                    verified = true;
                    foreach (Match metric in Regex.Matches(claim.Groups["metrics"].Value, MetricPattern, RegexOptions.IgnoreCase))
                    {
                        var index = metric.Value.Contains("波動") ? 0 : metric.Value.Equals("VaR", StringComparison.OrdinalIgnoreCase) ? 1
                            : metric.Value.Contains("回撤") ? 3 : 2;
                        var improvement = claim.Groups["improvement"].Success;
                        var a = improvement ? Improvement(baseline, left.Metrics!, index) : Metrics[index].Value(left.Metrics!);
                        var b = improvement ? Improvement(baseline, right.Metrics!, index) : Metrics[index].Value(right.Metrics!);
                        var valid = claim.Groups["direction"].Value switch {
                            "大於" or "高於" => a > b, "小於" or "低於" => a < b, _ => a == b
                        };
                        if (!valid) issues.Add("ScenarioComparisonDirectionMismatch");
                    }
                }
                if (!verified) issues.Add("UnverifiedScenarioComparison");
            }
        }
        return issues.ToArray();
    }

    private static PortfolioScenarioResult? Resolve(string text, PortfolioScenarioResult[] scenarios, bool lastMention)
    {
        // 名稱／目的地必須唯一；多個同類情境時「ETF／現金」不足以選擇其中一個。
        var mentions = new List<(PortfolioScenarioResult Scenario, int Position)>();
        foreach (var scenario in scenarios)
        {
            var aliases = new List<string> { scenario.Name };
            if (scenario.TransferToTicker is { } ticker && scenarios.Count(s => s.TransferToTicker == ticker) == 1) aliases.Add(ticker);
            if (scenario.RemainderPolicy == "cash" && scenarios.Count(s => s.RemainderPolicy == "cash") == 1) aliases.Add("現金");
            if (scenario.RemainderPolicy == "index_etf" && scenarios.Count(s => s.RemainderPolicy == "index_etf") == 1) aliases.Add("ETF");
            foreach (var alias in aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
                foreach (Match match in Regex.Matches(text, @"(?<![A-Za-z0-9])" + Regex.Escape(alias) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
                    mentions.Add((scenario, match.Index));
        }
        if (mentions.Count == 0) return null;
        if (!lastMention) return mentions.Select(m => m.Scenario).Distinct().Count() == 1 ? mentions[0].Scenario : null;
        var position = mentions.Max(m => m.Position);
        var latest = mentions.Where(m => m.Position == position).Select(m => m.Scenario).Distinct().ToArray();
        return latest.Length == 1 ? latest[0] : null;
    }
}
