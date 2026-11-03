using System.Text.Json;
using System.Text.Json.Nodes;
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
            "get_portfolio_snapshot" => "取得授權投組持倉、成本、權重、價格與損益快照。",
            "get_risk_report" => "重用同期間且輸入相符的已存風險結果；沒有時明確回 cache miss。",
            "run_risk_analysis" => "以快照補算 Core、TailRisk 或 RiskContribution；不可輸入數值資料。",
            "get_performance_attribution" => "取得持股與產業的正負績效貢獻。",
            "simulate_weight_scenario" => "比較最多三個假設目標權重情境，不改變持倉。",
            _ => "讀取使用者已保存的風險偏好；缺少時回 unavailable，不推測。"
        }, Schema(name))).ToArray();

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
            properties["scenarios"] = JsonNode.Parse("""{"type":"array","minItems":1,"maxItems":3,"items":{"type":"object","additionalProperties":false,"required":["name","targetWeights"],"properties":{"name":{"type":"string","maxLength":80},"targetWeights":{"type":"object","additionalProperties":{"type":"number","minimum":0,"maximum":1}},"remainderPolicy":{"type":"string","enum":["cash"]},"preserveOtherHoldings":{"type":"boolean","const":true}}}}""");
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
                if (scenarios.Any(x => x is not JsonObject obj || obj.Any(p => p.Key is not ("name" or "targetWeights" or "remainderPolicy" or "preserveOtherHoldings")))) return "UnexpectedScenarioArgument";
                PortfolioScenarioCalculator.ValidateRequests(scenarios.Deserialize<List<PortfolioScenarioRequest>>(AgentNodeJson.SerializerOptions));
                if (scope.DomainContext?[SnapshotKey] is null) return "SnapshotRequired";
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
        if (!await db.Portfolios.AsNoTracking().AnyAsync(x => x.Id == diagnosis.PortfolioId && x.OwnerUserId == scope.UserId && x.IsActive, ct))
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
                    patch[SnapshotKey] = board[SnapshotKey]?.DeepClone() ?? JsonSerializer.SerializeToNode(await EvaluatePortfolioScenariosNodeHandler.CaptureAsync(context, ct), AgentNodeJson.SerializerOptions);
                }
                catch (AgentWorkflowQueryException e) { warning = e.Message; }
                var total = inputs.Assets.Sum(x => x.Quantity * x.Prices.LastOrDefault());
                data = Object(new { diagnosis, inputs.CommonTradingDays, inputs.Warnings, warning,
                    holdings = inputs.Assets.Select(a => new { a.Ticker, a.Currency, a.Quantity, a.AverageCost,
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
                data = Object(new { analysis, results, inputs.CommonTradingDays, diagnosis });
                break;
            }
            case "get_performance_attribution":
            {
                await new CalculatePerformanceAttributionNodeHandler(serviceScope.ServiceProvider.GetRequiredService<IPortfolioValuationService>()).ExecuteAsync(context, ct);
                patch[AgentBlackboardKeys.PerformanceAttribution] = AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.PerformanceAttribution]!.DeepClone();
                data = Object(new { attribution = patch[AgentBlackboardKeys.PerformanceAttribution], diagnosis });
                break;
            }
            case "simulate_weight_scenario":
            {
                var snapshot = board[SnapshotKey]!.Deserialize<PortfolioScenarioSnapshot>(AgentNodeJson.SerializerOptions)!;
                var requests = call.Arguments["scenarios"]!.Deserialize<List<PortfolioScenarioRequest>>(AgentNodeJson.SerializerOptions)!;
                var comparison = PortfolioScenarioCalculator.Calculate(snapshot, requests);
                patch[PortfolioScenarioCalculator.ResultKey] = JsonSerializer.SerializeToNode(comparison, AgentNodeJson.SerializerOptions);
                data = Object(comparison with { Snapshot = null });
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

    private static JsonObject Object(object value) => JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions)!.AsObject();
}
