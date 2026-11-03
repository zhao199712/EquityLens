using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.PortfolioValuations;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>投組任務型唯讀工具；每次呼叫使用獨立 DbContext，數值只取自授權資料或已保存快照。</summary>
public sealed class PortfolioLoopTools(IServiceScopeFactory scopes) : IAgentLoopTools
{
    public const string SnapshotKey = "portfolioScenarioSnapshot";
    private static readonly string[] Analyses = ["Core", "TailRisk", "RiskContribution"];
    private static readonly string[] Names = ["get_portfolio_snapshot", "get_risk_report", "run_risk_analysis", "get_performance_attribution", "simulate_weight_scenario", "get_risk_profile"];

    public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => Names.Select(name => new LoopToolDefinition(name,
        name switch
        {
            "get_portfolio_snapshot" => "取得授權投組持倉、成本、權重、價格、損益與 accountCash 帳簿現金。只有 accountCash.status=Available 才可引用 balance；Unavailable 是未知而非零，持股權重不包含帳戶現金。indexEtfCandidates 列出核准指數 ETF 的 Ready／Unavailable 與原因，未持有候選不算目前持倉。",
            "get_risk_report" => "重用同期間且輸入相符的已存風險結果；沒有時明確回 cache miss。",
            "run_risk_analysis" => "以快照補算 Core、TailRisk 或 RiskContribution；不可輸入數值資料。",
            "get_performance_attribution" => "取得持股與產業的正負績效貢獻。",
            "simulate_weight_scenario" => "比較一至三個減碼情境；targetWeights 僅列減碼來源、使用 0 到 1 的最終權重且不得高於目前權重。cash 留現金不指定 transferToTicker；holding 必須指定另一檔既有持股 transferToTicker，不把轉入股放在 targetWeights。index_etf 可轉入 snapshot 中 Ready 的 0050／006208，不必已持有；不能指定其他新標的。未指定情境的優化問題比較留現金與一個可用 ETF；不改變持倉。",
            _ => "讀取使用者已保存的風險偏好；缺少時回 unavailable，不推測。"
        }, Schema(name))).ToArray();

    // 顯示值由同一份工具結果確定性產生，原始數值仍保留供 gate 驗證。
    internal static JsonObject DisplayMetrics(PortfolioRiskMetrics metrics)
    {
        static string? Percent(decimal? value) => value is null ? null
            : decimal.Round(value.Value * 100m, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture) + "%";
        return Object(new {
            annualizedVolatility = Percent(metrics.AnnualizedVolatility),
            portfolioVolatility = Percent(metrics.PortfolioVolatility),
            maxDrawdown = Percent(metrics.MaxDrawdown),
            historicalVaR = Percent(metrics.HistoricalVaR),
            expectedShortfall = Percent(metrics.ExpectedShortfall),
            contributions = metrics.Contributions?.Select(c => new { c.Ticker, weight = Percent(c.Weight), componentRiskShare = Percent(c.ComponentRiskShare) })
        });
    }

    private static JsonObject Schema(string name)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        if (name == "run_risk_analysis")
        {
            properties["analysis"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Analyses.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) };
            required.Add("analysis");
        }
        if (name == "simulate_weight_scenario")
        {
            properties["scenarios"] = JsonNode.Parse("""{"type":"array","minItems":1,"maxItems":3,"items":{"type":"object","additionalProperties":false,"required":["name","targetWeights"],"properties":{"name":{"type":"string","maxLength":80},"targetWeights":{"type":"object","additionalProperties":{"type":"number","minimum":0,"maximum":1}},"remainderPolicy":{"type":"string","enum":["cash","holding","index_etf"]},"preserveOtherHoldings":{"type":"boolean","const":true},"transferToTicker":{"type":["string","null"]}}}}""");
            required.Add("scenarios");
        }
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
    }

    public string? Validate(LoopToolScope scope, LoopToolCall call)
    {
        if (!Names.Contains(call.Tool)) return "ToolNotVisible";
        var allowed = call.Tool switch { "run_risk_analysis" => new[] { "analysis" }, "simulate_weight_scenario" => ["scenarios"], _ => [] };
        if (call.Arguments.Any(x => !allowed.Contains(x.Key))) return "UnexpectedArgument";
        try
        {
            if (call.Tool == "run_risk_analysis")
            {
                if (!Analyses.Contains(call.Arguments["analysis"]?.GetValue<string>())) return "AnalysisNotAllowed";
                if (scope.DomainContext?[AgentBlackboardKeys.MathInputs] is null) return "SnapshotRequired";
            }
            if (call.Tool == "simulate_weight_scenario")
            {
                if (call.Arguments["scenarios"] is not JsonArray scenarios) return "ScenariosRequired";
                if (scenarios.Any(x => x is not JsonObject obj || obj.Any(p => p.Key is not ("name" or "targetWeights" or "remainderPolicy" or "preserveOtherHoldings" or "transferToTicker")))) return "UnexpectedScenarioArgument";
                var requests = scenarios.Deserialize<List<PortfolioScenarioRequest>>(AgentNodeJson.SerializerOptions)!;
                if (requests.Any(r => r is null || r.TargetWeights is null)) return "scenarios[].targetWeights: non-empty object required";
                if (requests.Count is < 1 or > 3 || requests.Select(r => r.Name).Distinct().Count() != requests.Count)
                    return "scenarios: provide 1..3 uniquely named scenarios";
                var requested = scope.DomainContext?[AgentBlackboardKeys.RoutingContext]?["scenarios"]?.Deserialize<List<PortfolioScenarioRequest>>(AgentNodeJson.SerializerOptions);
                if (requested is { Count: > 0 } && (requested.Count != requests.Count || requested.Any(r => !requests.Any(a =>
                    a.RemainderPolicy == r.RemainderPolicy && a.TransferToTicker == r.TransferToTicker && a.TargetWeights.Count == r.TargetWeights.Count
                    && r.TargetWeights.All(w => a.TargetWeights.TryGetValue(w.Key, out var value) && value == w.Value))))) return "RequestedScenarioMismatch";
                if (scope.DomainContext?[SnapshotKey] is null) return "SnapshotRequired";
                var failures = requests.Select((r, i) => ScenarioError(r, i)).Where(x => x is not null).ToList();
                if (failures.Count == requests.Count || requested is { Count: > 0 } && failures.Count > 0)
                    return string.Join("; ", failures);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return "InvalidArguments"; }
        return null;
    }

    public async Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
    {
        var denied = Validate(scope, call);
        if (denied is not null) return new([], "Tool denied", denied);
        using var serviceScope = scopes.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
        var board = scope.DomainContext?.DeepClone().AsObject() ?? throw new InvalidOperationException("Portfolio context required.");
        var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var portfolio = await db.Portfolios.AsNoTracking()
            .Where(x => x.Id == diagnosis.PortfolioId && x.OwnerUserId == scope.UserId && x.IsActive)
            .Select(x => new { x.BaseCurrency }).SingleOrDefaultAsync(ct);
        if (portfolio is null)
            return new([], "Portfolio unavailable", "portfolio_not_found");
        var run = new AgentRun { Id = scope.RunId ?? Guid.Empty, UserId = scope.UserId, InputJson = scope.InputJson ?? "{}", BlackboardJson = board.ToJsonString() };
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id };
        var context = new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { });
        var patch = new JsonObject();
        JsonObject data;
        switch (call.Tool)
        {
            case "get_portfolio_snapshot":
            {
                var inputs = board[AgentBlackboardKeys.MathInputs]?.Deserialize<PortfolioRiskMathInputs>(AgentNodeJson.SerializerOptions)
                    ?? await serviceScope.ServiceProvider.GetRequiredService<IPortfolioRiskMathInputProvider>().PrepareAsync(context, ct);
                patch[AgentBlackboardKeys.MathInputs] = JsonSerializer.SerializeToNode(inputs, AgentNodeJson.SerializerOptions);
                string? warning = null;
                try
                {
                    patch[SnapshotKey] = board[SnapshotKey]?.DeepClone() ?? JsonSerializer.SerializeToNode(await PortfolioScenarioSnapshotReader.CaptureAsync(context, ct), AgentNodeJson.SerializerOptions);
                }
                catch (AgentWorkflowQueryException e) { warning = e.Message; }
                var namedAssets = patch[SnapshotKey]?.Deserialize<PortfolioScenarioSnapshot>(AgentNodeJson.SerializerOptions)?.Assets;
                var total = inputs.Assets.Sum(x => x.Quantity * x.Prices.LastOrDefault());
                var accountCash = await PortfolioAccountCashReader.ReadAsync(db, diagnosis.PortfolioId, portfolio.BaseCurrency, diagnosis.To, ct);
                data = Object(new { diagnosis, inputs.CommonTradingDays, inputs.Warnings, warning, accountCash,
                    indexEtfCandidates = patch[SnapshotKey]?["indexEtfCandidates"]?.DeepClone(),
                    holdingWeightBasis = "SecuritiesMarketValueExcludingAccountCash",
                    holdings = inputs.Assets.Select(a => new { a.Ticker, securityName = namedAssets?.FirstOrDefault(x => x.Ticker == a.Ticker)?.SecurityName, a.Currency, a.Quantity, a.AverageCost,
                        price = a.Prices.LastOrDefault(), weight = total > 0 ? a.Quantity * a.Prices.LastOrDefault() / total : (decimal?)null,
                        unrealizedProfit = a.Quantity * (a.Prices.LastOrDefault() - a.AverageCost) }) });
                break;
            }
            case "get_risk_report":
            {
                var resolved = await serviceScope.ServiceProvider.GetRequiredService<IPortfolioRiskRunResolver>().ResolveAsync(diagnosis, scope.UserId, ct);
                patch[AgentBlackboardKeys.MathResults] = JsonSerializer.SerializeToNode(resolved.Results, AgentNodeJson.SerializerOptions);
                patch[AgentBlackboardKeys.PortfolioRiskEvidence] = JsonSerializer.SerializeToNode(resolved.Evidence, AgentNodeJson.SerializerOptions);
                data = Object(new { evidence = resolved.Evidence, metrics = PortfolioRiskMetricsReader.FromMathResults(patch[AgentBlackboardKeys.MathResults]), diagnosis });
                break;
            }
            case "run_risk_analysis":
            {
                var inputs = board[AgentBlackboardKeys.MathInputs]!.Deserialize<PortfolioRiskMathInputs>(AgentNodeJson.SerializerOptions)!;
                var analysis = call.Arguments["analysis"]!.GetValue<string>();
                if (inputs.Assets.Count == 0 || inputs.CommonTradingDays < 2 || (analysis != "Core" && inputs.CommonTradingDays < 120))
                    return new([], "樣本不足，未產生風險估計。", Data: Object(new { status = "InsufficientHistory", analysis, inputs.CommonTradingDays }));
                string[] operations = analysis switch {
                    "Core" => ["calculate-portfolio-return", "calculate-concentration", "calculate-annualized-volatility", "calculate-max-drawdown"],
                    "TailRisk" => ["calculate-historical-var", "calculate-expected-shortfall"],
                    _ => ["calculate-portfolio-volatility", "calculate-volatility-risk-contribution"] };
                var executor = serviceScope.ServiceProvider.GetRequiredService<IPortfolioRiskMathExecutor>();
                var results = operations.Select(op => executor.Execute(op, inputs, new(), run.Id)).ToList();
                patch[AgentBlackboardKeys.MathResults] = JsonSerializer.SerializeToNode(results, AgentNodeJson.SerializerOptions);
                data = Object(new { analysis, display = DisplayMetrics(PortfolioRiskMetricsReader.FromMathResults(patch[AgentBlackboardKeys.MathResults])), results, inputs.CommonTradingDays, diagnosis });
                break;
            }
            case "get_performance_attribution":
            {
                var attribution = await new PortfolioAttributionReader(serviceScope.ServiceProvider.GetRequiredService<IPortfolioValuationService>()).ReadAsync(context, ct);
                patch[AgentBlackboardKeys.PerformanceAttribution] = JsonSerializer.SerializeToNode(attribution, AgentNodeJson.SerializerOptions);
                data = Object(new { attribution = patch[AgentBlackboardKeys.PerformanceAttribution], diagnosis });
                break;
            }
            case "simulate_weight_scenario":
            {
                var snapshot = board[SnapshotKey]!.Deserialize<PortfolioScenarioSnapshot>(AgentNodeJson.SerializerOptions)!;
                var requests = call.Arguments["scenarios"]!.Deserialize<List<PortfolioScenarioRequest>>(AgentNodeJson.SerializerOptions)!;
                var comparison = CalculateAvailable(snapshot, requests);
                patch[PortfolioScenarioCalculator.ResultKey] = JsonSerializer.SerializeToNode(comparison, AgentNodeJson.SerializerOptions);
                data = ScenarioEvidence(comparison);
                break;
            }
            default:
                // 目前沒有使用者偏好欄位；不得把治理警示當成使用者風險承受度。
                data = Object(new { available = false, reason = "尚未保存使用者風險偏好，請向使用者確認。" });
                patch["userRiskPreference"] = data.DeepClone();
                break;
        }
        return new([], call.Tool + " completed", Data: data, BlackboardPatch: patch);
    }

    private static string? ScenarioError(PortfolioScenarioRequest request, int index)
    {
        try { PortfolioScenarioCalculator.ValidateRequests([request]); return null; }
        catch (AgentWorkflowQueryException e)
        {
            return $"scenarios[{index}].targetWeights/remainderPolicy/transferToTicker: {e.Message} " +
                "targetWeights only contains reductions (fractions 0..1); holding requires transferToTicker for another existing holding; index_etf requires a Ready approved ETF (0050/006208); cash requires no destination.";
        }
    }

    internal static PortfolioScenarioComparison CalculateAvailable(PortfolioScenarioSnapshot snapshot, IReadOnlyList<PortfolioScenarioRequest> requests)
    {
        var accepted = new List<PortfolioScenarioRequest>();
        var errors = new List<string>();
        for (var i = 0; i < requests.Count; i++)
        {
            try
            {
                PortfolioScenarioCalculator.Calculate(snapshot, [requests[i]]);
                accepted.Add(requests[i]);
            }
            catch (AgentWorkflowQueryException e)
            {
                errors.Add(ScenarioError(requests[i], i) ?? $"scenarios[{i}].targetWeights/transferToTicker: {e.Message}");
            }
        }
        if (accepted.Count == 0) throw new AgentWorkflowQueryException("scenario_invalid", string.Join("; ", errors));
        var comparison = PortfolioScenarioCalculator.Calculate(snapshot, accepted);
        return comparison with { Warnings = comparison.Warnings.Concat(errors).ToArray(), ScenarioErrors = errors };
    }

    /// <summary>保留情境快照的期間及確定性權重差，不把原始價格陣列送入模型。</summary>
    internal static JsonObject ScenarioEvidence(PortfolioScenarioComparison comparison)
    {
        var data = new JsonObject { ["period"] = comparison.Snapshot is { } snapshot
            ? Object(new { from = snapshot.Dates.Count > 0 ? snapshot.Dates[0] : snapshot.From,
                to = snapshot.Dates.Count > 0 ? snapshot.Dates[^1] : snapshot.To,
                returnFrom = snapshot.Dates.Count > 1 ? (DateOnly?)snapshot.Dates[1] : null,
                requestedFrom = snapshot.From, requestedTo = snapshot.To }) : null };
        data["riskChanges"] = JsonSerializer.SerializeToNode(comparison.Scenarios.Select(s => new { scenarioName = s.Name,
            changes = PortfolioComparisonClaims.Changes(comparison, s) }), AgentNodeJson.SerializerOptions);
        data["riskComparisonStatements"] = JsonSerializer.SerializeToNode(PortfolioComparisonClaims.Statements(comparison));
        data["priceBasis"] = comparison.Snapshot?.PriceBasis;
        data["indexEtfCandidates"] = JsonSerializer.SerializeToNode(comparison.Snapshot?.IndexEtfCandidates, AgentNodeJson.SerializerOptions);
        foreach (var item in Object(comparison with { Snapshot = null })) data[item.Key] = item.Value?.DeepClone();
        if (comparison.Baseline is { } baseline && data["scenarios"] is JsonArray scenarios)
            for (var i = 0; i < comparison.Scenarios.Count; i++)
            {
                var scenario = comparison.Scenarios[i];
                scenarios[i]!["weightChanges"] = Object(scenario.Weights.ToDictionary(x => x.Key,
                    x => x.Value - baseline.Weights.GetValueOrDefault(x.Key)));
                scenarios[i]!["cashWeightChange"] = scenario.CashWeight - baseline.CashWeight;
            }
        return data;
    }

    private static JsonObject Object(object value) => JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions)!.AsObject();
}
