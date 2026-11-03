using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public sealed record PortfolioDiagnosisNarrativeInput(
    string PortfolioName,
    DateOnly From,
    DateOnly To,
    string? Objective,
    PortfolioPerformanceAttribution Attribution,
    PortfolioRiskMetrics Metrics,
    IReadOnlyList<RiskAnalysisPriority> Priorities,
    PortfolioScenarioComparison? ScenarioComparison = null);

public interface IPortfolioDiagnosisNarrativeAgent
{
    Task<string> GenerateAsync(PortfolioDiagnosisNarrativeInput input, CancellationToken cancellationToken = default);
}

public sealed class LlmPortfolioDiagnosisNarrativeAgent(IChatCompletionService chat) : IPortfolioDiagnosisNarrativeAgent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<string> GenerateAsync(PortfolioDiagnosisNarrativeInput input, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var result = await chat.CompleteAsync(new ChatCompletionRequest(
            SystemPrompt,
            BuildUserPrompt(input),
            0.3,
            1600,
            ChatResponseFormat.Text), timeout.Token);
        return result.Content.Trim();
    }

    private static string BuildUserPrompt(PortfolioDiagnosisNarrativeInput input) => JsonSerializer.Serialize(new
    {
        portfolio = input.PortfolioName,
        period = new { from = input.From.ToString("yyyy-MM-dd"), to = input.To.ToString("yyyy-MM-dd") },
        objective = input.Objective,
        performance = new
        {
            portfolioReturn = input.Attribution.PortfolioReturn,
            benchmarkReturn = input.Attribution.BenchmarkReturn,
            activeReturn = input.Attribution.ActiveReturn,
            coverage = input.Attribution.CoverageStatus,
            mainDrags = input.Attribution.Holdings.Take(3).Select(x => new { x.Name, x.Weight, x.Return, x.Contribution }),
            mainContributors = input.Attribution.Holdings.OrderByDescending(x => x.Contribution).Take(3).Select(x => new { x.Name, x.Weight, x.Return, x.Contribution })
        },
        riskMetrics = input.Metrics,
        riskMetricsMethod = "舊診斷：歷史固定股數價格序列；contributions 為當期固定權重對數報酬共變異數分解，與年化波動率數值可能不同。",
        scenarioComparison = input.ScenarioComparison is null ? null : new {
            input.ScenarioComparison.Status, input.ScenarioComparison.Method, input.ScenarioComparison.ObservationCount,
            input.ScenarioComparison.Baseline, input.ScenarioComparison.Scenarios, input.ScenarioComparison.Warnings
        },
        modelQuality = "Unvalidated",
        recommendedAnalyses = input.Priorities.Select(x => new { x.Priority, x.Analysis, x.Reason })
    }, Json);

    private const string SystemPrompt = """
        你是 EquityLens 的機構級投資組合風險分析師。使用者會提供一份已經計算完成的投組診斷證據（績效歸因、風險指標、建議補做分析），你要撰寫一段「風險解讀」。
        規則：
        1. 只根據提供的數字解讀，絕不可捏造未提供的數字、事件或結論。
        2. 解讀這些指標代表什麼意義：波動率與回撤反映的風險程度、集中度（HHI／最大持倉權重）代表的分散程度、VaR／ES 的潛在損失意涵，以及相對大盤報酬的意涵。
        3. 指出風險集中在哪裡，並說明主要拖累與貢獻來源。
        4. 全部使用臺灣繁體中文（zh-TW），語氣中性、客觀、分析性。
        5. 輸出 2 到 4 段的 markdown 文字，可用 **粗體** 強調重點；不要逐條報流水帳，要有意涵與因果的解讀。
        6. 不得提供投資建議、買賣指令或保證性用語。
        7. volatilityRiskShare 是全體持股合計，絕不能解讀為某一持股風險貢獻。只有 contributions 的逐股數據能支持風險來源判斷；績效歸因的貢獻不等於風險貢獻。
        8. VaR 等於 ES 不足以證明沒有厚尾；短樣本下不作分布形狀推論。資料齊全也不代表模型已通過回測。
        9. 只引用 scenarioComparison 實際提供的情境數字；狀態 Unavailable 或 InsufficientHistory 時明示限制，不得拿現行診斷數字冒充情境結果。情境是固定權重歷史假設，不是實際持倉績效或預測。即使波動降低也不可保證報酬或所有風險均降低。
        10. 情境比較只能以 scenarioComparison.baseline 為基準，必須明示它的波動率；不可與 riskMetrics 的舊診斷數字交叉計算差異。兩者方法不同。逐股波動貢獻的加總是當期固定權重共變異數風險，不一定等於舊診斷歷史固定股數波動率。
        11. 若某項指標為 null（資料不足），就明確說明該項資料不足，不要推估。
        """;
}
