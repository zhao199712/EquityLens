using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>投組角色使用共用有界迴圈；只將已保存的工具事實交給品質關卡。</summary>
public sealed class PortfolioAgentLoopNodeHandler(IChatCompletionService chat, PortfolioLoopTools tools,
    IOptions<ResearchAgentLoopOptions> options, TimeProvider clock, AgentMethodCatalog? skills = null) : IAgentNodeHandler
{
    public string NodeType => "PortfolioAgentLoop";
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var state = await new AgentLoopEngine(chat, tools, options, clock, portfolio: true, skills: skills).RunAsync(context, "Portfolio", cancellationToken);
        if (context.AwaitingApproval) return;
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(state.Answer, AgentNodeJson.SerializerOptions);
        board["portfolioLoopStopReason"] = state.StopReason;
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = AgentNodeJson.Serialize(new { state.Answer, state.StopReason, state.Turn, state.ToolCalls, state.GuardDenials });
    }
}

/// <summary>確定性檢查工具事實與模型主張，結構化數字直接來自保存結果。</summary>
public sealed class PortfolioQualityGateNodeHandler : IAgentNodeHandler
{
    public string NodeType => "PortfolioQualityGate";
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var answer = board["portfolioLoopAnswer"]?.Deserialize<LoopAnswer>(AgentNodeJson.SerializerOptions);
        var facts = board["agentLoopFacts"]?.Deserialize<List<LoopDataFact>>(AgentNodeJson.SerializerOptions) ?? [];
        var issues = Check(answer, facts);
        var attribution = board[AgentBlackboardKeys.PerformanceAttribution]?.Deserialize<PortfolioPerformanceAttribution>(AgentNodeJson.SerializerOptions);
        var metrics = PortfolioRiskMetricsReader.FromMathResults(board[AgentBlackboardKeys.MathResults]);
        var comparison = board[PortfolioScenarioCalculator.ResultKey]?.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions);
        if (RequiresScenario(board) && comparison?.Status != "Calculated")
            issues.Add((board[AgentBlackboardKeys.MathInputs]?["commonTradingDays"]?.GetValue<int>() ?? 0) >= 120 ? "OptimizationScenarioRequired" : "OptimizationDataUnavailable");
        var limited = issues.Count > 0 || attribution?.CoverageStatus != "complete";
        var summary = issues.Count > 0 ? "模型答案未通過品質檢查；以下僅列出已保存的工具資料，請確認資料限制。" : answer!.Markdown;
        if (issues.Contains("OptimizationScenarioRequired")) summary = "優化問題尚未取得同一快照的完整情境比較，無法提供改善幅度。";
        if (issues.Contains("OptimizationDataUnavailable")) summary = "共同歷史資料不足，無法比較優化前後的波動、VaR、ES 與最大回撤；請補足資料。";
        if (comparison is not null) summary = ScenarioSummary(comparison);
        var output = new PortfolioDiagnosisOutput(summary, attribution?.PortfolioReturn, attribution?.BenchmarkReturn, attribution?.ActiveReturn,
            attribution?.Holdings.Where(x => x.Contribution < 0).OrderBy(x => x.Contribution).Take(3).ToArray() ?? [],
            attribution?.Holdings.Where(x => x.Contribution > 0).OrderByDescending(x => x.Contribution).Take(3).ToArray() ?? [], [],
            limited ? "Limited" : "complete", metrics, issues.Count == 0 && comparison is null ? answer?.Markdown : null, comparison, diagnosis,
            "Unvalidated", board[AgentBlackboardKeys.MathInputs]?["commonTradingDays"]?.GetValue<int>(),
            "既有持股歷史重建；情境採固定權重、含現金的共同快照；模型未經回測驗證", attribution?.CoverageStatus,
            (board[AgentBlackboardKeys.MathResults] as JsonArray)?.OfType<JsonObject>()
                .Select(x => new PortfolioMetricBasis(x["operation"]?.ToString() ?? "unknown", x["provenance"]?["measurementBasis"]?.DeepClone())).ToList());
        board["portfolioQualityGate"] = JsonSerializer.SerializeToNode(new { passed = issues.Count == 0, blockingIssues = issues, limited }, AgentNodeJson.SerializerOptions);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioDiagnosisDraft, output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = board["portfolioQualityGate"]!.ToJsonString();
        context.AddEvent(context.Run, context.Node, "PortfolioQualityGateEvaluated", issues.Count == 0 ? "Passed" : "Limited", new { issues });
        return Task.CompletedTask;
    }

    internal static bool RequiresScenario(JsonObject board) => Regex.IsMatch(
        board[AgentBlackboardKeys.RoutingContext]?["objective"]?.GetValue<string>() ?? "",
        @"優化|改善|降低?波動|降低.{0,8}風險|降低.{0,8}波動|optimi[sz]|reduce.{0,20}(?:risk|volatility)", RegexOptions.IgnoreCase)
        || board[AgentBlackboardKeys.RoutingContext]?["scenarios"] is JsonArray { Count: > 0 };

    internal static string ScenarioSummary(PortfolioScenarioComparison comparison)
    {
        static string Metrics(ScenarioMetrics m) => $"年化波動 {m.AnnualizedVolatility:P2}、VaR {m.HistoricalVaR:P2}、ES {m.ExpectedShortfall:P2}、最大回撤 {m.HypotheticalMaxDrawdown:P2}";
        var baseline = comparison.Baseline?.Metrics is { } b ? "同一快照原組合：" + Metrics(b) + "。" : "";
        return "以下為唯讀情境，未變更實際持倉。" + baseline + string.Join("；", comparison.Scenarios.Select(s =>
            $"{s.Name}：減碼資金{(s.RemainderPolicy == "holding" ? "轉入 " + s.TransferToTicker : "留作現金")}；" +
            string.Join("、", s.Weights.Select(w => $"{w.Key} 權重 {w.Value:P2}")) + $"；現金 {s.CashWeight:P2}" +
            (s.Metrics is { } m ? "；調整後：" + Metrics(m) : "") +
            (s.DifferenceFromBaseline is { } d ? "；相較原組合差異（百分點）：" + Metrics(d) : ""))) +
            (comparison.Status == "Calculated" ? "。情境不代表未來績效或保證改善。" : "。資料不足，風險指標無法計算。") +
            $"方法：{comparison.Method}；有效日報酬 {comparison.ObservationCount} 筆、VaR 信心水準 {comparison.ConfidenceLevel:P0}、期間 {comparison.HorizonDays} 日。";
    }

    internal static List<string> Check(LoopAnswer? answer, IReadOnlyList<LoopDataFact> facts)
    {
        var issues = new List<string>();
        if (answer is null || string.IsNullOrWhiteSpace(answer.Markdown) || answer.Markdown == AgentLoopEngine.Refusal) return ["InsufficientEvidence"];
        var byId = facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (facts.Count == 0) issues.Add("MissingToolEvidence");
        if (answer.CitedEvidenceIds.Any(x => !byId.ContainsKey(x))) issues.Add("UnknownToolEvidence");
        foreach (var sentence in Regex.Split(answer.Markdown, @"[。！？\n]"))
        {
            var ids = Regex.Matches(sentence, @"\[(P\d+)\]").Select(x => x.Groups[1].Value).ToArray();
            if (ids.Any(x => !byId.ContainsKey(x))) issues.Add("UnknownToolEvidence");
            var text = Regex.Replace(sentence, @"\[P\d+\]", "");
            var citations = ids.Where(byId.ContainsKey).SelectMany(id => Values(byId[id].Data)).ToHashSet(StringComparer.Ordinal);
            foreach (Match number in Regex.Matches(text, @"(?<![A-Za-z\d])[-+]?\d+(?:,\d{3})*(?:\.\d+)?\s*[%％]?"))
            {
                if (!decimal.TryParse(number.Value.Trim().TrimEnd('%', '％'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
                var percent = number.Value.TrimEnd().EndsWith('%') || number.Value.TrimEnd().EndsWith('％');
                if (!citations.Contains(Key(value, percent))) issues.Add("UntraceableNumbers");
            }
        }
        if (Regex.IsMatch(answer.Markdown, @"(?<!不)(?<!無法)(?<!不能)保證獲利")) issues.Add("GuaranteedProfitClaim");
        return issues.Distinct().ToList();
    }

    private static string Key(decimal value, bool percent) => (percent ? "p:" : "n:") + value.ToString("G29", CultureInfo.InvariantCulture);
    private static IEnumerable<string> Values(JsonNode? node)
    {
        if (node is JsonObject obj) { foreach (var item in obj) foreach (var v in Values(item.Value)) yield return v; }
        else if (node is JsonArray array) { foreach (var item in array) foreach (var v in Values(item)) yield return v; }
        else if (node is JsonValue scalar)
        {
            if (decimal.TryParse(scalar.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                yield return Key(number, false);
                yield return Key(decimal.Round(number * 100, 2), true);
                yield return Key(decimal.Round(number * 100, 1), true);
                yield return Key(decimal.Round(number * 100, 4), true);
            }
        }
    }
}
