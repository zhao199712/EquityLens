using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record PortfolioDiagnosisContext(Guid PortfolioId, string PortfolioName, string BaseCurrency, DateOnly From, DateOnly To, int HoldingCount);
public sealed record AttributionItem(string EvidenceId, string Name, string? Industry, decimal Weight, decimal Return, decimal Contribution);
public sealed record PortfolioPerformanceAttribution(decimal? PortfolioReturn, decimal? BenchmarkReturn, decimal? ActiveReturn, int PricedHoldingCount, int HoldingCount, IReadOnlyList<AttributionItem> Holdings, IReadOnlyList<AttributionItem> Industries, string CoverageStatus);
public sealed record RiskAnalysisPriority(string EvidenceId, int Priority, string Analysis, string Reason);
public sealed record PortfolioRiskProfileSnapshot(bool Available, string DataStatus, DateOnly? DataAsOfDate, int CommonTradingDays, int AlertCount);
public sealed record PortfolioRiskMetrics(
    decimal? AnnualizedVolatility,
    decimal? MaxDrawdown,
    decimal? HistoricalVaR,
    decimal? ExpectedShortfall,
    decimal? PortfolioVolatility,
    decimal? ConcentrationHhi,
    decimal? LargestWeight,
    decimal? VolatilityRiskShare,
    IReadOnlyList<PortfolioHoldingRiskContribution>? Contributions = null);
public sealed record PortfolioDiagnosisOutput(string Summary, decimal? PortfolioReturn, decimal? BenchmarkReturn, decimal? ActiveReturn, IReadOnlyList<AttributionItem> MainDrags, IReadOnlyList<AttributionItem> MainContributors, IReadOnlyList<RiskAnalysisPriority> RecommendedAnalyses, string EvidenceStatus, PortfolioRiskMetrics RiskMetrics, string? Interpretation,
    PortfolioScenarioComparison? ScenarioComparison = null,
    PortfolioDiagnosisContext? AnalysisPeriod = null,
    string ModelQuality = "Unvalidated",
    int? ObservationCount = null,
    string? Method = null);
public sealed record PortfolioDiagnosisGap(string Code, bool Resolvable, string? RequiredCapability, string Reason);
public sealed record PortfolioDiagnosisQualityResult(
    string Status,
    int Iteration,
    IReadOnlyList<PortfolioDiagnosisGap> Gaps,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> CompletedCapabilities,
    PortfolioRiskMetrics Metrics);

public static class PortfolioDiagnosisQualityStatuses
{
    public const string Pass = "Pass";
    public const string NeedsAnalysis = "NeedsAnalysis";
    public const string Limited = "Limited";
}

internal static class PortfolioRiskMetricsReader
{
    public static PortfolioRiskMetrics FromMathResults(JsonNode? mathResults)
    {
        decimal? annualizedVolatility = null, maxDrawdown = null, historicalVaR = null, expectedShortfall = null, portfolioVolatility = null, hhi = null, largestWeight = null, riskShare = null;
        IReadOnlyList<PortfolioHoldingRiskContribution>? contributions = null;
        if (mathResults is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is not JsonObject result) continue;
                var value = result["value"];
                switch (result["operation"]?.GetValue<string>())
                {
                    case "calculate-annualized-volatility": annualizedVolatility = AsDecimal(value); break;
                    case "calculate-max-drawdown": maxDrawdown = AsDecimal(value); break;
                    case "calculate-historical-var": historicalVaR = AsDecimal(value); break;
                    case "calculate-expected-shortfall": expectedShortfall = AsDecimal(value); break;
                    case "calculate-portfolio-volatility": portfolioVolatility = AsDecimal(value); break;
                    case "calculate-concentration": hhi = Field(value, "hhi"); largestWeight = Field(value, "largestWeight"); break;
                    case "calculate-volatility-risk-contribution":
                        riskShare = Field(value, "componentRiskShare");
                        contributions = value?["holdings"]?.Deserialize<List<PortfolioHoldingRiskContribution>>(AgentNodeJson.SerializerOptions);
                        break;
                }
            }
        }
        return new(annualizedVolatility, maxDrawdown, historicalVaR, expectedShortfall, portfolioVolatility, hhi, largestWeight, riskShare, contributions);
    }

    private static decimal? Field(JsonNode? node, string name) => node is JsonObject obj ? AsDecimal(obj[name]) : null;

    private static decimal? AsDecimal(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var parsed)) return parsed;
        return value.TryGetValue<double>(out var floating) ? (decimal)floating : null;
    }
}

internal static class PortfolioDiagnosisBlackboard
{
    public static PortfolioDiagnosisContext Context(JsonObject board) => JsonSerializer.Deserialize<PortfolioDiagnosisContext>(board[AgentBlackboardKeys.PortfolioContext]?.ToJsonString() ?? throw new AgentNodeException("blackboard_key_missing", AgentNodeErrorCategories.ValidationFailure, $"Blackboard key '{AgentBlackboardKeys.PortfolioContext}' is missing."), AgentNodeJson.SerializerOptions)!;
    public static T Required<T>(JsonObject board, string key) => JsonSerializer.Deserialize<T>(board[key]?.ToJsonString() ?? throw new AgentNodeException("blackboard_key_missing", AgentNodeErrorCategories.ValidationFailure, $"Blackboard key '{key}' is missing."), AgentNodeJson.SerializerOptions)!;
    public static void Set<T>(JsonObject board, string key, T value) => board[key] = JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions);
}

public sealed class LoadPortfolioDiagnosisContextNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.LoadContext;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        using var input = JsonDocument.Parse(context.Run.InputJson);
        var root = input.RootElement;
        var portfolioId = root.GetProperty("portfolioId").GetGuid();
        var from = DateOnly.Parse(root.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(root.GetProperty("to").GetString()!);
        var portfolio = await context.DbContext.Portfolios.Include(x => x.Holdings).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == portfolioId && x.OwnerUserId == context.Run.UserId && x.IsActive, cancellationToken)
            ?? throw new AgentNodeException("portfolio_not_found", AgentNodeErrorCategories.PermanentFailure, "Portfolio was not found.", retryable: false);
        var output = new PortfolioDiagnosisContext(portfolio.Id, portfolio.Name, portfolio.BaseCurrency, from, to, portfolio.Holdings.Count);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioContext, output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.InputJson = AgentNodeJson.Serialize(new { portfolioId, from, to });
        context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Portfolio diagnosis context loaded.", new { portfolioId, from, to, holdingCount = output.HoldingCount });
    }
}

internal sealed class PortfolioAttributionReader
{
    private readonly IPortfolioValuationService _valuations;
    public PortfolioAttributionReader(IPortfolioValuationService valuations) => _valuations = valuations;
    public async Task<PortfolioPerformanceAttribution> ReadAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var historyResult = await _valuations.GetValuationHistoryForUserAsync(diagnosis.PortfolioId, context.Run.UserId, diagnosis.From, diagnosis.To, cancellationToken);
        if (!historyResult.IsSuccess || historyResult.Value is null) throw new AgentNodeException("valuation_history_unavailable", AgentNodeErrorCategories.TransientFailure, historyResult.ErrorMessage ?? "Portfolio valuation history is unavailable.", retryable: true);
        var portfolio = await context.DbContext.Portfolios.Include(x => x.Holdings).ThenInclude(x => x.Security).AsNoTracking()
            .SingleAsync(x => x.Id == diagnosis.PortfolioId, cancellationToken);
        var ids = portfolio.Holdings.Select(x => x.SecurityId).ToList();
        var fromUtc = DateTime.SpecifyKind(diagnosis.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(diagnosis.To.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        var prices = await context.DbContext.MarketPrices.AsNoTracking().Where(x => ids.Contains(x.SecurityId) && x.Interval == "1d" && x.PriceTime >= fromUtc && x.PriceTime <= toUtc)
            .Select(x => new { x.SecurityId, x.PriceTime, x.Close, x.AdjustedClose }).ToListAsync(cancellationToken);
        var raw = new List<(string Name, string? Industry, decimal Value, decimal Return)>();
        foreach (var holding in portfolio.Holdings)
        {
            var series = prices.Where(x => x.SecurityId == holding.SecurityId).OrderBy(x => x.PriceTime).ToList();
            if (series.Count < 2) continue;
            var first = series[0].AdjustedClose ?? series[0].Close; var last = series[^1].AdjustedClose ?? series[^1].Close;
            if (first <= 0m) continue;
            raw.Add(($"{holding.Security.Ticker} {holding.Security.Name}".Trim(), holding.Security.Industry ?? holding.Security.Sector, holding.Quantity * first, last / first - 1m));
        }
        var total = raw.Sum(x => x.Value);
        var holdings = raw.Select((x, i) => new AttributionItem($"holding-{i + 1}", x.Name, x.Industry, total == 0m ? 0m : x.Value / total, x.Return, total == 0m ? 0m : x.Value / total * x.Return)).OrderBy(x => x.Contribution).ToList();
        var industries = holdings.GroupBy(x => string.IsNullOrWhiteSpace(x.Industry) ? "未分類" : x.Industry!).Select((g, i) => new AttributionItem($"industry-{i + 1}", g.Key, g.Key, g.Sum(x => x.Weight), g.Sum(x => x.Weight) == 0m ? 0m : g.Sum(x => x.Contribution) / g.Sum(x => x.Weight), g.Sum(x => x.Contribution))).OrderBy(x => x.Contribution).ToList();
        var history = historyResult.Value;
        var output = new PortfolioPerformanceAttribution(history.Twr, history.BenchmarkReturn, history.ExcessReturn, holdings.Count, portfolio.Holdings.Count, holdings, industries, holdings.Count == portfolio.Holdings.Count ? "complete" : "partial");
        return output;
    }
}

public sealed class FinalizePortfolioDiagnosisNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.FinalizeDiagnosis;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson); var output = PortfolioDiagnosisBlackboard.Required<PortfolioDiagnosisOutput>(board, AgentBlackboardKeys.PortfolioDiagnosisDraft);
        context.Node.InputJson = AgentNodeJson.Serialize(new { evidenceStatus = output.EvidenceStatus, priorityCount = output.RecommendedAnalyses.Count }); context.Node.OutputJson = AgentNodeJson.Serialize(output); PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.FinalOutput, output); context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Run.OutputJson = context.Node.OutputJson; context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Final portfolio diagnosis written.", new { output.EvidenceStatus }); return Task.CompletedTask;
    }
}

public sealed class FinalizeRejectedPortfolioDiagnosisNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var approval = AgentNodeJson.GetBlackboardObject(board, AgentBlackboardKeys.HumanApproval) ?? new JsonObject();
        var decision = approval[HumanApprovalFields.Decision]?.GetValue<string>() ?? HumanApprovalDecisions.Rejected;
        var comment = approval[HumanApprovalFields.Comment]?.GetValue<string>();
        var reviewerId = approval[HumanApprovalFields.ReviewerId]?.GetValue<string>();
        var output = new { rejected = true, decision, comment, reviewerId };
        context.Node.InputJson = AgentNodeJson.Serialize(new { decision, comment });
        context.Node.OutputJson = AgentNodeJson.Serialize(output);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.FinalOutput, output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Run.OutputJson = context.Node.OutputJson;
        context.AddEvent(context.Run, context.Node, AgentEventTypes.RunFailed, "Portfolio diagnosis rejected by human reviewer.", new { decision, comment });
        throw new AgentNodeException("portfolio_diagnosis_rejected", AgentNodeErrorCategories.PermanentFailure,
            string.IsNullOrWhiteSpace(comment) ? "人工拒絕此投組診斷。" : $"人工拒絕此投組診斷：{comment}", retryable: false);
    }
}
