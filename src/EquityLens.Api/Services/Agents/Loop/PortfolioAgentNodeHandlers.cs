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
        var rejectedNumbers = new List<string>();
        var issues = Check(answer, facts, rejectedNumbers);
        var attribution = board[AgentBlackboardKeys.PerformanceAttribution]?.Deserialize<PortfolioPerformanceAttribution>(AgentNodeJson.SerializerOptions);
        var metrics = PortfolioRiskMetricsReader.FromMathResults(board[AgentBlackboardKeys.MathResults]);
        var comparison = board[PortfolioScenarioCalculator.ResultKey]?.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions);
        if (RequiresScenario(board) && comparison?.Status != "Calculated")
            issues.Add((board[AgentBlackboardKeys.MathInputs]?["commonTradingDays"]?.GetValue<int>() ?? 0) >= 120 ? "OptimizationScenarioRequired" : "OptimizationDataUnavailable");
        if (comparison?.ScenarioErrors is { Count: > 0 }) issues.Add("PartialScenarioFailure");
        var limited = issues.Count > 0 || attribution?.CoverageStatus != "complete";
        var summary = issues.Count > 0 ? "模型答案未通過品質檢查；以下僅列出已保存的工具資料，請確認資料限制。" : answer!.Markdown;
        if (issues.Contains("OptimizationScenarioRequired")) summary = "優化問題尚未取得同一快照的完整情境比較，無法提供改善幅度。";
        if (issues.Contains("OptimizationDataUnavailable")) summary = "共同歷史資料不足，無法比較優化前後的波動、VaR、ES 與最大回撤；請補足資料。";
        if (comparison is not null) summary = ScenarioSummary(comparison);
        var output = new PortfolioDiagnosisOutput(summary, attribution?.PortfolioReturn, attribution?.BenchmarkReturn, attribution?.ActiveReturn,
            attribution?.Holdings.Where(x => x.Contribution < 0).OrderBy(x => x.Contribution).Take(3).ToArray() ?? [],
            attribution?.Holdings.Where(x => x.Contribution > 0).OrderByDescending(x => x.Contribution).Take(3).ToArray() ?? [], [],
            limited ? "Limited" : "complete", metrics, issues.Count == 0 ? answer?.Markdown : comparison?.Status == "Calculated" && issues.All(i => i is "UntraceableNumbers" or "MixedRiskBasis") ? VerifiedParagraphs(answer, facts) : null, comparison, diagnosis,
            "Unvalidated", board[AgentBlackboardKeys.MathInputs]?["commonTradingDays"]?.GetValue<int>(),
            "既有持股歷史重建；情境採固定權重、含現金的共同快照；模型未經回測驗證", attribution?.CoverageStatus,
            (board[AgentBlackboardKeys.MathResults] as JsonArray)?.OfType<JsonObject>()
                .Select(x => new PortfolioMetricBasis(x["operation"]?.ToString() ?? "unknown", x["provenance"]?["measurementBasis"]?.DeepClone())).ToList(), issues);
        board["portfolioQualityGate"] = JsonSerializer.SerializeToNode(new { passed = issues.Count == 0, blockingIssues = issues, rejectedNumbers, limited }, AgentNodeJson.SerializerOptions);
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
            (s.DifferenceFromBaseline is { } d ? "；相較原組合差異（百分點）：" + $"年化波動 {d.AnnualizedVolatility * 100:F2}、VaR {d.HistoricalVaR * 100:F2}、ES {d.ExpectedShortfall * 100:F2}、最大回撤 {d.HypotheticalMaxDrawdown * 100:F2}" : ""))) +
            (comparison.Status == "Calculated" ? "。情境不代表未來績效或保證改善。" : "。資料不足，風險指標無法計算。") +
            $"方法：{comparison.Method}；有效日報酬 {comparison.ObservationCount} 筆、VaR 信心水準 {comparison.ConfidenceLevel:P0}、期間 {comparison.HorizonDays} 日。";
    }

    internal static List<string> Check(LoopAnswer? answer, IReadOnlyList<LoopDataFact> facts, List<string>? rejectedNumbers = null)
    {
        var issues = new List<string>();
        if (answer is null || string.IsNullOrWhiteSpace(answer.Markdown) || answer.Markdown == AgentLoopEngine.Refusal) return ["InsufficientEvidence"];
        var byId = facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (facts.Count == 0) issues.Add("MissingToolEvidence");
        if (answer.CitedEvidenceIds.Any(x => !byId.ContainsKey(x))) issues.Add("UnknownToolEvidence");
        foreach (var sentence in Regex.Split(WithTableCitations(answer.Markdown.Normalize(System.Text.NormalizationForm.FormKC).Replace('−', '-')), @"[\n]"))
        {
            var ids = Regex.Matches(sentence, @"\[(P\d+)\]").Select(x => x.Groups[1].Value).ToArray();
            if (ids.Any(x => !byId.ContainsKey(x))) issues.Add("UnknownToolEvidence");
            var text = Regex.Replace(sentence, @"\[P\d+\]", "");
            // Markdown 編號不是數據；標題裡真正的指標數字仍須引用。
            text = Regex.Replace(text, @"^\s*(?:#{1,6}\s*)?\d+[.)、]\s+", "");
            var cited = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            // 帳戶餘額只能由明確帳簿欄位支持，不能借用情境 cashWeight 或其他指標碰巧相同的數字。
            foreach (Match cashClaim in Regex.Matches(text,
                @"(?:帳戶現金(?:餘額)?|帳簿現金(?:餘額)?|現金餘額)\s*(?:為|是|[:：|])?\s*(?:[A-Z]{3}|NT\$|新[台臺]幣)?\s*(?<value>[-+]?\d+(?:,\d{3})*(?:\.\d+)?)(?![\d.]|\s*[%％])"))
            {
                var token = cashClaim.Groups["value"].Value;
                if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) continue;
                var precision = token.Contains('.') ? token.Length - token.IndexOf('.') - 1 : 0;
                var supported = cited.Any(f => f.Tool == "get_portfolio_snapshot"
                    && f.Data["accountCash"] is JsonObject cash
                    && cash["status"]?.ToString() == "Available"
                    && cash["balance"] is JsonValue balance && balance.TryGetValue<decimal>(out var value)
                    && Matches(value, amount, false, precision));
                if (!supported) { issues.Add("UntraceableNumbers"); rejectedNumbers?.Add(token); }
            }
            // 日期是分析範圍而非金額；必須在本句引用的事實中確實存在。
            text = Regex.Replace(text, @"(?<!\d)20\d{2}-\d{2}-\d{2}(?!\d)", m =>
                cited.Any(f => f.Data.ToJsonString().Contains(m.Value, StringComparison.Ordinal)) ? "日期" : m.Value);
            var tickers = facts.SelectMany(f => Tickers(f.Data)).ToHashSet(StringComparer.Ordinal);
            text = Regex.Replace(text, @"(?<![\d.])\d{4,6}(?![\d.]|\s*(?:%|％|元|股|百分點))", m => tickers.Contains(m.Value) ? "標的" : m.Value);
            // 「1.47 與 0.90 個百分點」共用尾端單位；僅限相鄰數字串列，不跨指標文字借用單位。
            const string listNumber = @"[-+]?\d+(?:,\d{3})*(?:\.\d+)?";
            text = Regex.Replace(text, $@"(?<![\d.])(?<numbers>{listNumber}(?:\s*(?:與|和|及|、|或|~|～|至)\s*{listNumber})+)\s*(?<unit>個百分點|百分點)",
                m => Regex.Replace(m.Groups["numbers"].Value, listNumber, n => n.Value + m.Groups["unit"].Value));
            // 解釋兩種口徑不可互換，不等於把它們相減；但實際數字箭頭／改善幅度仍須同一快照。
            var quantitativeChange = Regex.IsMatch(text, @"\d[^。；;\n]{0,15}→[^。；;\n]{0,15}\d|(?:下降|降低|增加|改善|差額|差異為|降至|增至)[^。；;\n]{0,12}[-+]?\d");
            var separatesBases = !quantitativeChange && Regex.IsMatch(text,
                @"(?:口徑|基礎|基準).{0,8}不同|(?:不宜|不可|不能).{0,12}(?:比較|相減|替換|互換|相加)");
            var comparisonClaim = !separatesBases && facts.Any(f => f.Tool == "simulate_weight_scenario")
                && Regex.IsMatch(text, @"(?<![A-Za-z\d])[-+]?\d+(?:\.\d+)?")
                && Regex.IsMatch(text, @"波動|VaR|ES|回撤", RegexOptions.IgnoreCase)
                && (quantitativeChange || Regex.IsMatch(text, @"比較|差異|差額|前後")
                    || (text.Contains('|') && cited.Any(f => f.Tool == "simulate_weight_scenario")));
            if (comparisonClaim && cited.Any(f => f.Tool is "run_risk_analysis" or "get_risk_report")) issues.Add("MixedRiskBasis");
            var citations = cited.Where(f => !comparisonClaim || f.Tool == "simulate_weight_scenario").SelectMany(f => Values(f.Data)).ToArray();
            foreach (Match number in Regex.Matches(text, @"(?<![A-Za-z\d])(?<value>[-+]?\d+(?:,\d{3})*(?:\.\d+)?)(?:\s*(?<unit>[%％]|個百分點|百分點))?"))
            {
                var token = number.Groups["value"].Value;
                if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
                var percent = number.Groups["unit"].Success;
                var precision = token.Contains('.') ? token.Length - token.IndexOf('.') - 1 : 0;
                // 依顯示的小數位四捨五入，沒有任意相對誤差；整數主張仍須精確。
                if (!citations.Any(v => Matches(v, value, percent, precision)))
                { issues.Add("UntraceableNumbers"); rejectedNumbers?.Add(number.Value); }
            }
        }
        if (Regex.IsMatch(answer.Markdown, @"(?<!不)(?<!無法)(?<!不能)保證獲利")) issues.Add("GuaranteedProfitClaim");
        return issues.Distinct().ToList();
    }

    internal static string RepairHints(IReadOnlyList<string> rejected, IReadOnlyList<LoopDataFact> facts, LoopAnswer? answer = null)
    {
        var hints = new List<string>();
        if (answer is not null)
        {
            var lines = WithTableCitations(answer.Markdown.Normalize(System.Text.NormalizationForm.FormKC).Replace('−', '-')).Split('\n');
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var ids = Regex.Matches(line, @"\[(P\d+)\]").Select(m => m.Groups[1].Value).Distinct().ToArray();
                var problems = Check(new LoopAnswer(line, ids), facts);
                if (problems.Count == 0) continue;
                var sources = facts.Where(f => ids.Contains(f.Id)).Select(f => $"[{f.Id}]={f.Tool}");
                hints.Add("問題段落: " + AgentNodeJson.Trim(line, 350) + "；問題=" + string.Join(",", problems)
                    + "；目前引用=" + string.Join(",", sources)
                    + "。逐句補正確來源；風險偏好不能支持風險數字或權重。無法核對就移除非必要數字，不要猜算合計或改引用湊數。");
                if (hints.Count >= 6) break;
            }
        }
        // 日期字串不是數值候選，回報完整日期與真正含有日期的來源供模型核對。
        if (answer is not null)
            foreach (var date in Regex.Matches(answer.Markdown, @"(?<!\d)20\d{2}-\d{2}-\d{2}(?!\d)").Select(m => m.Value).Distinct().Take(8))
            {
                if (!date.Split('-').Any(part => rejected.Contains(part) || rejected.Contains(part.TrimStart('0')))) continue;
                var sources = facts.Where(f => f.Data.ToJsonString().Contains(date, StringComparison.Ordinal)).Select(f => "[" + f.Id + "]");
                hints.Add("日期 " + date + " exact sources: " + string.Join(", ", sources) + "；核對該工具的期間語義，標題亦需引用；無來源就移除。");
            }
        foreach (var token in rejected.Distinct().Take(12))
        {
            var match = Regex.Match(token, @"[-+]?\d+(?:,\d{3})*(?:\.\d+)?");
            if (!decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var claim)) continue;
            if (claim < -1_000_000_000_000m || claim > 1_000_000_000_000m) continue;
            var percent = token.Contains('%') || token.Contains('％') || token.Contains("百分點");
            var decimals = match.Value.Contains('.') ? match.Value.Length - match.Value.IndexOf('.') - 1 : 0;
            // 優先修正該數字原本引用的來源，避免另一口徑的近似數字誘導模型換引用。
            var sourceIds = answer is null ? [] : Regex.Split(WithTableCitations(answer.Markdown.Normalize(System.Text.NormalizationForm.FormKC).Replace('−', '-')), @"\n")
                .Where(line => line.Contains(token, StringComparison.Ordinal))
                .SelectMany(line => Regex.Matches(line, @"\[(P\d+)\]").Select(m => m.Groups[1].Value)).Distinct().ToArray();
            var candidateFacts = sourceIds.Length == 0 ? facts : facts.Where(f => sourceIds.Contains(f.Id)).ToArray();
            var candidates = candidateFacts.SelectMany(f => Values(f.Data).Select(v => (f.Id, Raw: v)))
                .Where(v => Math.Abs(v.Raw) < 1_000_000_000_000m)
                .Select(v => (v.Id, v.Raw, Display: percent ? v.Raw * 100m : v.Raw))
                .Where(v => Math.Abs(v.Display - claim) <= Math.Max(Math.Abs(claim) * .05m, .0001m))
                .OrderBy(v => Math.Abs(v.Display - claim)).DistinctBy(v => (v.Id, v.Raw)).Take(3)
                .Select(v => $"[{v.Id}] raw={v.Raw.ToString(CultureInfo.InvariantCulture)}, rounded={decimal.Round(v.Display, Math.Min(decimals, 8), MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}{(percent ? "%/百分點" : "")}");
            hints.Add(token + " candidates (核對語義後引用，不可只挑相同數字): " + string.Join("; ", candidates));
        }
        return string.Join("\n", hints);
    }

    private static string? VerifiedParagraphs(LoopAnswer? answer, IReadOnlyList<LoopDataFact> facts)
    {
        if (answer is null || answer.Markdown == AgentLoopEngine.Refusal) return null;
        var valid = Regex.Split(WithTableCitations(answer.Markdown), @"\n\s*\n")
            .Where(p => Regex.IsMatch(p, @"\[P\d+\]") && !p.Contains('|')
                && Check(new LoopAnswer(p, Regex.Matches(p, @"\[(P\d+)\]").Select(m => m.Groups[1].Value).Distinct().ToArray()), facts).Count == 0).ToArray();
        return valid.Length == 0 ? null : "以下保留通過數字與引用檢查的說明；其餘草稿已省略：\n\n" + string.Join("\n\n", valid);
    }

    private static string WithTableCitations(string markdown)
    {
        var lines = markdown.Split('\n');
        // 小標題與其緊接的引用段落屬同一說明區塊，不把標題的配置數字當成孤立主張。
        for (var h = 0; h < lines.Length; h++)
        {
            if (!Regex.IsMatch(lines[h], @"^\s*(?:#{1,6}\s|\*\*.+\*\*\s*$)") || Regex.IsMatch(lines[h], @"\[P\d+\]")) continue;
            var next = h + 1; while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
            if (next < lines.Length && !Regex.IsMatch(lines[next], @"^\s*#"))
                lines[h] += string.Concat(Regex.Matches(lines[next], @"\[P\d+\]").Select(m => m.Value).Distinct());
        }
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith('|')) continue;
            var start = i;
            while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('|')) i++;
            var previous = start - 1; while (previous >= 0 && string.IsNullOrWhiteSpace(lines[previous])) previous--;
            var next = i + 1; while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
            var source = previous >= 0 ? lines[previous] : "";
            if (next < lines.Length && Regex.IsMatch(lines[next], @"^(?:以上|表中|上述|來源|Source|\[P\d+\])")) source += lines[next];
            var refs = string.Concat(Regex.Matches(source, @"\[P\d+\]").Select(m => m.Value).Distinct());
            // 表頭的情境名稱屬於本表；只繼承本表資料列明列的來源，不擴散到其他段落。
            if (!Regex.IsMatch(lines[start], @"\[P\d+\]"))
                lines[start] += string.Concat(lines.Skip(start + 1).Take(i - start)
                    .SelectMany(line => Regex.Matches(line, @"\[P\d+\]").Select(m => m.Value)).Distinct());
            for (var row = start; row <= i; row++)
                if (!Regex.IsMatch(lines[row], @"\[P\d+\]")) lines[row] += refs;
        }
        return string.Join('\n', lines);
    }

    private static IEnumerable<string> Tickers(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var item in obj)
            {
                if (item.Key == "ticker" && item.Value is JsonValue ticker) yield return ticker.ToString();
                foreach (var value in Tickers(item.Value)) yield return value;
            }
        else if (node is JsonArray array) foreach (var item in array) foreach (var value in Tickers(item)) yield return value;
    }

    private static bool Matches(decimal evidence, decimal claim, bool percent, int precision)
    {
        try
        {
            var value = percent ? evidence * 100m : evidence;
            return value == claim || ((precision > 0 || percent) && precision <= 8 && decimal.Round(value, precision, MidpointRounding.AwayFromZero) == claim);
        }
        catch (OverflowException) { return false; }
    }

    private static IEnumerable<decimal> Values(JsonNode? node)
    {
        if (node is JsonObject obj) {
            foreach (var item in obj) {
                if (item.Key == "method" && item.Value is JsonValue method)
                    foreach (Match m in Regex.Matches(method.ToString(), @"(?:cashReturn|annualization)=([0-9]+)(?:;|$)"))
                        yield return decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                foreach (var v in Values(item.Value)) yield return v;
            }
        }
        else if (node is JsonArray array) { foreach (var item in array) foreach (var v in Values(item)) yield return v; }
        else if (node is JsonValue scalar && decimal.TryParse(scalar.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            yield return number;
    }
}
