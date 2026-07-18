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
public sealed record PortfolioDiagnosisOutput(string Summary, decimal? PortfolioReturn, decimal? BenchmarkReturn, decimal? ActiveReturn, IReadOnlyList<AttributionItem> MainDrags, IReadOnlyList<AttributionItem> MainContributors, IReadOnlyList<RiskAnalysisPriority> RecommendedAnalyses, string EvidenceStatus);

internal static class PortfolioDiagnosisBlackboard
{
    public static PortfolioDiagnosisContext Context(JsonObject board) => JsonSerializer.Deserialize<PortfolioDiagnosisContext>(board[AgentBlackboardKeys.PortfolioContext]?.ToJsonString() ?? throw new InvalidOperationException("Portfolio diagnosis context is missing."), AgentNodeJson.SerializerOptions)!;
    public static T Required<T>(JsonObject board, string key) => JsonSerializer.Deserialize<T>(board[key]?.ToJsonString() ?? throw new InvalidOperationException($"Blackboard key '{key}' is missing."), AgentNodeJson.SerializerOptions)!;
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
            .FirstOrDefaultAsync(x => x.Id == portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio was not found.");
        var output = new PortfolioDiagnosisContext(portfolio.Id, portfolio.Name, portfolio.BaseCurrency, from, to, portfolio.Holdings.Count);
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioContext, output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.InputJson = AgentNodeJson.Serialize(new { portfolioId, from, to });
        context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Portfolio diagnosis context loaded.", new { portfolioId, from, to, holdingCount = output.HoldingCount });
    }
}

public sealed class CalculatePerformanceAttributionNodeHandler : IAgentNodeHandler
{
    private readonly IPortfolioValuationService _valuations;
    public CalculatePerformanceAttributionNodeHandler(IPortfolioValuationService valuations) => _valuations = valuations;
    public string NodeType => PortfolioDiagnosisNodeTypes.CalculateAttribution;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var historyResult = await _valuations.GetValuationHistoryForUserAsync(diagnosis.PortfolioId, context.Run.UserId, diagnosis.From, diagnosis.To, cancellationToken);
        if (!historyResult.IsSuccess || historyResult.Value is null) throw new InvalidOperationException(historyResult.ErrorMessage ?? "Portfolio valuation history is unavailable.");
        var portfolio = await context.DbContext.Portfolios.Include(x => x.Holdings).ThenInclude(x => x.Security).AsNoTracking()
            .SingleAsync(x => x.Id == diagnosis.PortfolioId, cancellationToken);
        var ids = portfolio.Holdings.Select(x => x.SecurityId).ToList();
        var prices = await context.DbContext.MarketPrices.AsNoTracking().Where(x => ids.Contains(x.SecurityId) && x.Interval == "1d" && x.PriceTime.Date >= diagnosis.From.ToDateTime(TimeOnly.MinValue) && x.PriceTime.Date <= diagnosis.To.ToDateTime(TimeOnly.MaxValue))
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
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PerformanceAttribution, output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.InputJson = AgentNodeJson.Serialize(new { diagnosis.PortfolioId, diagnosis.From, diagnosis.To }); context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Performance attribution snapshot written.", new { output.PricedHoldingCount, output.HoldingCount, output.CoverageStatus });
    }
}

public sealed class LoadRiskProfileNodeHandler : IAgentNodeHandler
{
    private readonly IRiskAnalysisService _risk;
    public LoadRiskProfileNodeHandler(IRiskAnalysisService risk) => _risk = risk;
    public string NodeType => PortfolioDiagnosisNodeTypes.LoadRiskProfile;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson); var diagnosis = PortfolioDiagnosisBlackboard.Context(board);
        var result = await _risk.GetPortfolioRiskGovernanceAsync(diagnosis.PortfolioId, context.Run.UserId, cancellationToken);
        var output = result.IsSuccess && result.Value is not null
            ? new PortfolioRiskProfileSnapshot(true, result.Value.DataStatus, result.Value.DataAsOfDate, result.Value.CommonTradingDays, result.Value.Alerts.Count)
            : new PortfolioRiskProfileSnapshot(false, "unavailable", null, 0, 0);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.RiskProfile, output); context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Risk profile snapshot written.", new { output.Available });
    }
}

public sealed class PrioritizeRiskAnalysesNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson); var attribution = PortfolioDiagnosisBlackboard.Required<PortfolioPerformanceAttribution>(board, AgentBlackboardKeys.PerformanceAttribution);
        var profile = board[AgentBlackboardKeys.RiskProfile]?.AsObject(); var priorities = new List<RiskAnalysisPriority>();
        if (attribution.CoverageStatus != "complete") priorities.Add(new("risk-data-coverage", 1, "補齊持倉價格資料", "部分持倉缺少足夠的區間價格，歸因結果僅為部分覆蓋。"));
        var days = profile?["commonTradingDays"]?.GetValue<int>() ?? 0;
        if (days < 120) priorities.Add(new("risk-history", priorities.Count + 1, "延長並補齊歷史價格", "共同交易日不足 120 日，VaR 與壓力測試的統計穩定性有限。"));
        var alertCount = profile?["alertCount"]?.GetValue<int>() ?? 0;
        if (alertCount > 0) priorities.Add(new("risk-governance", priorities.Count + 1, "檢視集中度與治理警示", "現有風險治理檢查出現警示，應先確認集中曝險與資料品質。"));
        priorities.Add(new("risk-backtest", priorities.Count + 1, "執行 VaR 回測", "以背景回測驗證模型在實際資料上的覆蓋率；此分析不阻塞診斷報告。"));
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.RiskAnalysisPriorities, priorities); context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(priorities);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Risk analysis priorities written.", new { count = priorities.Count }); return Task.CompletedTask;
    }
}

public sealed class BuildPortfolioEvidencePacketNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.BuildEvidencePacket;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson); var packet = new { context = PortfolioDiagnosisBlackboard.Context(board), attribution = PortfolioDiagnosisBlackboard.Required<PortfolioPerformanceAttribution>(board, AgentBlackboardKeys.PerformanceAttribution), riskProfile = board[AgentBlackboardKeys.RiskProfile]?.DeepClone(), priorities = PortfolioDiagnosisBlackboard.Required<List<RiskAnalysisPriority>>(board, AgentBlackboardKeys.RiskAnalysisPriorities) };
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioEvidencePacket, packet); context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(packet); context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Portfolio evidence packet snapshot written.", null); return Task.CompletedTask;
    }
}

public sealed class DraftPortfolioDiagnosisNodeHandler : IAgentNodeHandler
{
    public string NodeType => PortfolioDiagnosisNodeTypes.DraftDiagnosis;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson); var attribution = PortfolioDiagnosisBlackboard.Required<PortfolioPerformanceAttribution>(board, AgentBlackboardKeys.PerformanceAttribution); var priorities = PortfolioDiagnosisBlackboard.Required<List<RiskAnalysisPriority>>(board, AgentBlackboardKeys.RiskAnalysisPriorities);
        var drags = attribution.Holdings.Take(3).ToList(); var gains = attribution.Holdings.OrderByDescending(x => x.Contribution).Take(3).ToList();
        var summary = attribution.ActiveReturn is null ? "基準或投組報酬資料不足，無法判定相對大盤表現。" : $"本期投組相對基準報酬為 {attribution.ActiveReturn:P2}。主要拖累與貢獻依可取得價格的持倉近似計算，資料覆蓋狀態為 {attribution.CoverageStatus}。";
        var output = new PortfolioDiagnosisOutput(summary, attribution.PortfolioReturn, attribution.BenchmarkReturn, attribution.ActiveReturn, drags, gains, priorities, attribution.CoverageStatus);
        PortfolioDiagnosisBlackboard.Set(board, AgentBlackboardKeys.PortfolioDiagnosisDraft, output); context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(output); context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "Portfolio diagnosis draft written from evidence packet.", new { dragCount = drags.Count, priorityCount = priorities.Count }); return Task.CompletedTask;
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
