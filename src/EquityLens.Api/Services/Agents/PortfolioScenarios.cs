using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record PortfolioHoldingRiskContribution(string Ticker, decimal Weight, decimal ComponentVolatility, decimal ComponentRiskShare);
public sealed record ScenarioAsset(string Ticker, decimal Quantity, decimal ValuationPrice, IReadOnlyList<decimal> Prices);
public sealed record PortfolioScenarioSnapshot(Guid PortfolioId, DateTime CapturedAtUtc, string Currency, DateOnly From, DateOnly To,
    IReadOnlyList<DateOnly> Dates, IReadOnlyList<ScenarioAsset> Assets, decimal Cash, IReadOnlyList<string> Warnings);
public sealed record ScenarioMetrics(decimal AnnualizedVolatility, decimal HistoricalVaR, decimal ExpectedShortfall, decimal HypotheticalMaxDrawdown,
    IReadOnlyList<PortfolioHoldingRiskContribution> Contributions);
public sealed record PortfolioScenarioResult(string Name, string Status, IReadOnlyDictionary<string, decimal> Weights,
    IReadOnlyDictionary<string, decimal> Quantities, decimal CashWeight, decimal EquityHhi, decimal LargestEquityWeight,
    ScenarioMetrics? Metrics, ScenarioMetrics? DifferenceFromBaseline, string RemainderPolicy = "cash", string? TransferToTicker = null);
public sealed record PortfolioScenarioComparison(string Status, string Method, string InputHash, PortfolioScenarioSnapshot? Snapshot,
    int ObservationCount, decimal ConfidenceLevel, int HorizonDays, PortfolioScenarioResult? Baseline,
    IReadOnlyList<PortfolioScenarioResult> Scenarios, IReadOnlyList<string> Warnings);

/// <summary>以同一快照比較減碼留現金或轉入既有持股情境，不變更帳戶持倉。</summary>
public static class PortfolioScenarioCalculator
{
    public const string ToolName = "evaluate_portfolio_scenario";
    public const string ResultKey = "portfolioScenarioComparison";
    public const string Method = "FixedWeightHistoricalSimpleReturns; cashReturn=0; annualization=252; equityHhi excludes cash";

    public static void ValidateRequests(IReadOnlyList<PortfolioScenarioRequest>? requests)
    {
        if (requests is null) return;
        if (requests.Any(x => x is null)) Invalid("情境內容不可為空。");
        if (requests.Count is < 1 or > 3) Invalid("請提供一至三個明確的減碼情境。");
        if (requests.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != requests.Count) Invalid("情境名稱不可重複。");
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80 || request.TargetWeights is null || request.TargetWeights.Count == 0)
                Invalid("情境須包含名稱、股票代號與目標權重。");
            if (request.RemainderPolicy is not ("cash" or "holding") || !request.PreserveOtherHoldings) Invalid("僅支援減碼留現金或轉入指定既有持股，其他持股股數不變。");
            if (request.RemainderPolicy == "holding" && (string.IsNullOrWhiteSpace(request.TransferToTicker) || request.TargetWeights!.ContainsKey(request.TransferToTicker)))
                Invalid("轉入情境須指定不同於減碼標的的既有持股。");
            if (request.RemainderPolicy == "cash" && request.TransferToTicker is not null) Invalid("現金情境不可同時指定轉入持股。");
            if (request.TargetWeights!.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value < 0 || x.Value > 1) || request.TargetWeights.Values.Sum() > 1)
                Invalid("權重須介於 0 與 1，合計不得超過 1。");
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid(string message) => throw new AgentWorkflowQueryException("scenario_invalid", message);

    public static PortfolioScenarioComparison Calculate(PortfolioScenarioSnapshot snapshot, IReadOnlyList<PortfolioScenarioRequest> requests)
    {
        ValidateRequests(requests);
        if (snapshot.Cash < 0 || snapshot.Assets.Any(a => a.Quantity < 0 || a.ValuationPrice <= 0) || snapshot.Assets.Select(a => a.Ticker).Distinct().Count() != snapshot.Assets.Count)
            Invalid("情境不支援負現金、放空、重複代號或無效價格。");
        if (snapshot.Dates.Count < 2 || snapshot.Assets.Any(a => a.Prices.Count != snapshot.Dates.Count || a.Prices.Any(p => p <= 0)))
            Invalid("所有持股需要相同交易日的有效價格，不可排除缺價股票後重配權重。");
        var nav = snapshot.Cash + snapshot.Assets.Sum(a => a.Quantity * a.ValuationPrice);
        if (nav <= 0) Invalid("投組淨值必須大於零。");
        var baseWeights = snapshot.Assets.ToDictionary(a => a.Ticker, a => a.Quantity * a.ValuationPrice / nav);
        var observations = snapshot.Dates.Count - 1;
        var returns = snapshot.Assets.Select(a => Enumerable.Range(1, a.Prices.Count - 1).Select(i => a.Prices[i] / a.Prices[i - 1] - 1).ToArray()).ToArray();
        var covariance = returns.Select(x => returns.Select(y => RiskMath.CalculateCovariance(x, y)).ToArray()).ToArray();
        PortfolioScenarioResult Evaluate(string name, Dictionary<string, decimal> weights)
        {
            var w = snapshot.Assets.Select(a => weights[a.Ticker]).ToArray();
            var cashWeight = 1 - w.Sum();
            ScenarioMetrics? metrics = null;
            if (observations >= 120)
            {
                var daily = Enumerable.Range(0, observations).Select(t => w.Select((weight, i) => weight * returns[i][t]).Sum()).ToArray();
                var values = new List<decimal> { 1m };
                foreach (var value in daily) values.Add(values[^1] * (1 + value));
                var contributions = snapshot.Assets.Select((a, i) => {
                    var risk = RiskMath.CalculateVolatilityRiskContribution(w, covariance, [i]);
                    return new PortfolioHoldingRiskContribution(a.Ticker, w[i], risk.ComponentVolatility, risk.ComponentRiskShare);
                }).ToList();
                metrics = new(RiskMath.CalculateAnnualizedVolatility(RiskMath.CalculateVolatility(daily)),
                    RiskMath.CalculateHistoricalVaR(daily, .95m), RiskMath.CalculateExpectedShortfall(daily, .95m),
                    RiskMath.CalculateMaxDrawdown(values), contributions);
            }
            return new(name, metrics is null ? "InsufficientHistory" : "Calculated", weights,
                snapshot.Assets.ToDictionary(a => a.Ticker, a => weights[a.Ticker] == baseWeights[a.Ticker] ? a.Quantity : weights[a.Ticker] * nav / a.ValuationPrice),
                cashWeight, w.Sum(x => x * x), w.DefaultIfEmpty(0m).Max(), metrics, null);
        }
        var baseline = Evaluate("原組合", baseWeights);
        var scenarios = new List<PortfolioScenarioResult>();
        foreach (var request in requests)
        {
            var weights = new Dictionary<string, decimal>(baseWeights);
            foreach (var (ticker, target) in request.TargetWeights)
            {
                if (!baseWeights.TryGetValue(ticker, out var current)) Invalid($"{ticker} 不在持倉中；請指定既有股票代號。");
                if (target > current) Invalid($"{ticker} 目標權重高於原權重；請只指定減碼來源權重，轉入持股另行指定。");
                weights[ticker] = target;
            }
            if (request.RemainderPolicy == "holding")
            {
                if (!baseWeights.ContainsKey(request.TransferToTicker!)) Invalid("轉入標的不在既有持股中，請先澄清。");
                var released = request.TargetWeights.Sum(x => baseWeights[x.Key] - x.Value);
                weights[request.TransferToTicker!] += released;
            }
            var result = Evaluate(request.Name, weights) with { RemainderPolicy = request.RemainderPolicy, TransferToTicker = request.TransferToTicker,
                CashWeight = request.RemainderPolicy == "holding" ? snapshot.Cash / nav : 1 - weights.Values.Sum() };
            if (result.Metrics is { } m && baseline.Metrics is { } b)
                result = result with { DifferenceFromBaseline = new(m.AnnualizedVolatility - b.AnnualizedVolatility,
                    m.HistoricalVaR - b.HistoricalVaR, m.ExpectedShortfall - b.ExpectedShortfall,
                    m.HypotheticalMaxDrawdown - b.HypotheticalMaxDrawdown, []) };
            scenarios.Add(result);
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AgentNodeJson.Serialize(new { snapshot, requests }))));
        var warnings = snapshot.Warnings.Concat(observations < 120 ? new[] { "InsufficientHistory: 至少需要120筆共同日報酬；僅顯示配置。" } : Array.Empty<string>()).ToList();
        warnings.Add("假設固定權重歷史報酬序列，並非帳戶實際績效；現金零報酬，不含費用、滑價及稅負。模型未經回測驗證。");
        return new(baseline.Status, Method, hash, snapshot, observations, .95m, 1, baseline, scenarios, warnings);
    }
}

/// <summary>唯讀取得授權投組的情境輸入快照。</summary>
internal static class PortfolioScenarioSnapshotReader
{
    internal static async Task<PortfolioScenarioSnapshot> CaptureAsync(AgentNodeExecutionContext context, CancellationToken ct)
    {
        var diagnosis = PortfolioDiagnosisBlackboard.Context(AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson));
        var db = context.DbContext;
        var portfolio = await db.Portfolios.Include(p => p.Holdings).ThenInclude(h => h.Security).AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == diagnosis.PortfolioId && p.OwnerUserId == context.Run.UserId && p.IsActive, ct)
            ?? throw new AgentWorkflowQueryException("scenario_unavailable", "投組不存在或無權存取。");
        var holdings = portfolio.Holdings.Where(h => h.Quantity != 0).OrderBy(h => h.Security.Ticker).ToList();
        if (holdings.Count == 0 || holdings.Any(h => h.Quantity < 0 || h.Security.Currency != portfolio.BaseCurrency))
            throw new AgentWorkflowQueryException("scenario_unavailable", "目前只支援同幣別、多頭且有持倉的投組。");
        var ids = holdings.Select(h => h.SecurityId).ToArray();
        var from = DateTime.SpecifyKind(diagnosis.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(diagnosis.To.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        var prices = await db.MarketPrices.AsNoTracking().Where(p => ids.Contains(p.SecurityId) && p.Interval == "1d" && p.PriceTime >= from && p.PriceTime <= to).OrderBy(p => p.PriceTime).ToListAsync(ct);
        var maps = holdings.Select(h => prices.Where(p => p.SecurityId == h.SecurityId && p.Close > 0 && (p.AdjustedClose ?? p.Close) > 0)
            .GroupBy(p => DateOnly.FromDateTime(p.PriceTime)).ToDictionary(g => g.Key, g => g.Last())).ToList();
        var dates = maps.Select(m => m.Keys.AsEnumerable()).Aggregate((a, b) => a.Intersect(b)).Order().TakeLast(253).ToArray();
        if (dates.Length < 2) throw new AgentWorkflowQueryException("scenario_unavailable", "所有持股缺少共同價格資料；不會排除股票後重新配重。");
        var capturedAt = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(capturedAt);
        var transactions = await db.PortfolioTransactions.AsNoTracking().Where(t => t.PortfolioId == portfolio.Id && t.TransactionDate <= today).ToListAsync(ct);
        var flows = await db.PortfolioCashFlows.AsNoTracking().Where(f => f.PortfolioId == portfolio.Id && f.EffectiveDate <= today && f.Status != "Skipped" && f.Status != "Scheduled").ToListAsync(ct);
        if (flows.Any(f => f.Currency != portfolio.BaseCurrency)) throw new AgentWorkflowQueryException("scenario_unavailable", "現金流包含不同幣別，情境不進行推估匯率換算。");
        var cash = transactions.Sum(t => t.TransactionType == "BUY" ? -(t.Quantity * t.Price + t.Fee) : t.Quantity * t.Price - t.Fee)
            + flows.Sum(f => f.FlowType is "Withdrawal" or "Fee" or "DividendTax" ? -f.Amount : f.Amount);
        var warnings = new List<string>();
        if (prices.Any(p => p.AdjustedClose is null)) warnings.Add("部分資料無調整後價格，使用收盤價；公司行動影響需另行確認。");
        return new(portfolio.Id, capturedAt, portfolio.BaseCurrency, diagnosis.From, diagnosis.To, dates,
            holdings.Select((h, i) => new ScenarioAsset(h.Security.Ticker, h.Quantity, maps[i][dates[^1]].Close,
                dates.Select(d => maps[i][d].AdjustedClose ?? maps[i][d].Close).ToArray())).ToList(), cash, warnings);
    }
}
