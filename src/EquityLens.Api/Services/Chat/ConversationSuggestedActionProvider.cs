using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Chat;

/// <summary>依授權標的、已保存的工具結果及品質狀態挑選追問；不呼叫模型或執行工具。</summary>
public sealed class ConversationSuggestedActionProvider(EquityLensDbContext db, WorkflowExecutionCompatibility compatibility)
{
    public async Task<IReadOnlyList<ConversationSuggestedActionDto>> ForRunAsync(
        Guid userId, Guid sessionId, AgentRun run, string? answer, CancellationToken ct = default)
    {
        if (run.UserId != userId || run.Status != AgentRunStatuses.Succeeded || string.IsNullOrWhiteSpace(answer)
            || !string.IsNullOrWhiteSpace(run.ErrorMessage) || run.Nodes.Count == 0 || compatibility.IsRetired(run)) return [];
        var session = await db.ChatSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (session is null) return [];
        var latest = await db.ChatMessages.AsNoTracking().Where(x => x.ChatSessionId == sessionId)
            .OrderByDescending(x => x.SequenceNumber).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (latest?.Role != "assistant" || latest.AgentRunId != run.Id || latest.SequenceNumber is null) return [];
        try
        {
            // Reset 與後續回覆均使舊卡片失去選項，重新載入也不會恢復。
            if (!Guid.TryParse(JsonNode.Parse(session.ContextJson)?["lastAgentRunId"]?.GetValue<string>(), out var last) || last != run.Id) return [];
            var board = JsonNode.Parse(run.BlackboardJson) as JsonObject;
            var output = JsonNode.Parse(run.OutputJson ?? "{}") as JsonObject;
            if (board is null || output is null || output["errorCode"] is not null) return [];
            return run.WorkflowType switch
            {
                AgentWorkflowTypes.PortfolioDiagnosis => await PortfolioAsync(userId, run, board, output, ct),
                AgentWorkflowTypes.ResearchAgent => await ResearchAsync(userId, run, output, ct),
                _ => []
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            // 不相容的歷史資料只保留唯讀答案，不猜測下一步。
            return [];
        }
    }

    private async Task<IReadOnlyList<ConversationSuggestedActionDto>> PortfolioAsync(
        Guid userId, AgentRun run, JsonObject board, JsonObject output, CancellationToken ct)
    {
        var gate = board["portfolioQualityGate"];
        if (gate?["passed"]?.GetValue<bool>() != true || gate["limited"]?.GetValue<bool>() != false
            || gate["blockingIssues"] is not JsonArray { Count: 0 } || output["evidenceStatus"]?.GetValue<string>() != "complete") return [];
        var input = PortfolioDiagnosisWorkflowDefinitionProvider.ParseInput(run.InputJson);
        var context = board[AgentBlackboardKeys.PortfolioContext]?.Deserialize<PortfolioDiagnosisContext>(AgentNodeJson.SerializerOptions);
        if (context is null || context.PortfolioId != input.PortfolioId || context.From != input.From || context.To != input.To) return [];
        var portfolios = await db.Portfolios.AsNoTracking().Where(x => x.OwnerUserId == userId && x.IsActive)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var portfolio = portfolios.SingleOrDefault(x => x.Id == input.PortfolioId);
        if (portfolio is null || string.IsNullOrWhiteSpace(portfolio.Name)) return [];
        var prefix = $"接著，針對投資組合「{portfolio.Name}」，";
        var prefixEn = $"For portfolio “{portfolio.Name}”, ";
        ConversationSuggestedActionDto Action(string id, string label, string prompt, string labelEn, string promptEn) =>
            new(id, label, prefix + prompt, labelEn, prefixEn + promptEn);

        var facts = board["agentLoopFacts"] as JsonArray ?? [];
        bool UsedTool(string tool) => facts.OfType<JsonObject>().Any(f => f["tool"]?.GetValue<string>() == tool);
        var profileAction = Action("portfolio-profile-fit", "符合我的風險偏好嗎？", "目前的風險水準是否符合我的風險偏好？請引用偏好來源並說明差距。",
            "Does this fit my risk preference?", "does the current risk level fit my risk preference? Cite the preference source and explain any gap.");

        IReadOnlyList<ConversationSuggestedActionDto> actions;
        if (board[PortfolioScenarioCalculator.ResultKey] is JsonNode comparisonNode)
        {
            var comparison = comparisonNode.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions);
            if (comparison?.Status != "Calculated" || comparison.Snapshot?.PortfolioId != portfolio.Id
                || comparison.Baseline?.Metrics is null || comparison.ScenarioErrors is { Count: > 0 }
                || comparison.Scenarios is not { Count: > 0 }
                || comparison.Scenarios.Any(x => x.Status != "Calculated" || x.Metrics is null)) return [];
            actions = [
                Action("scenario-remaining-risks", "調整後還有哪些風險？", "請根據已完成的情境比較，說明調整後仍存在的風險與限制。",
                    "What risks remain after the adjustment?", "explain the remaining risks and limitations using the completed scenario comparison."),
                Action("scenario-metrics", "這些指標代表什麼？", "請解釋已完成情境比較中的波動率、VaR、ES 與最大回撤，以及各指標的適用限制。",
                    "What do these metrics mean?", "explain volatility, VaR, ES and maximum drawdown in the completed scenario comparison, including their limitations."),
                .. UsedTool("get_risk_profile") ? Array.Empty<ConversationSuggestedActionDto>() : [Action("scenario-profile-fit", "哪個方案較符合我的風險偏好？",
                    "請根據已完成的情境比較與我的風險偏好，說明各方案與偏好的差距。",
                    "Which option better fits my risk preference?", "compare each option in the completed scenario comparison with my risk preference and explain the gaps.")]
            ];
        }
        else
        {
            // Core 與貢獻可能分次計算，以保存的所有工具結果判斷。
            var results = new JsonArray();
            void AddResults(JsonNode? source)
            {
                if (source is JsonArray array)
                    foreach (var item in array) results.Add(item?.DeepClone());
            }
            AddResults(board[AgentBlackboardKeys.MathResults]);
            var hasContributions = false;
            var hasTailRisk = false;
            foreach (var fact in facts.OfType<JsonObject>())
            {
                if (fact["tool"]?.GetValue<string>() is not ("run_risk_analysis" or "get_risk_report")) continue;
                AddResults(fact["data"]?["results"]);
                var reported = fact["data"]?["metrics"];
                hasContributions |= reported?["contributions"] is JsonArray { Count: > 0 };
                hasTailRisk |= reported?["historicalVaR"] is JsonValue || reported?["expectedShortfall"] is JsonValue;
            }
            var metrics = PortfolioRiskMetricsReader.FromMathResults(results);
            if (metrics.AnnualizedVolatility is null && metrics.PortfolioVolatility is null) return [];
            hasContributions |= metrics.Contributions is { Count: > 0 };
            hasTailRisk |= metrics.HistoricalVaR is not null || metrics.ExpectedShortfall is not null;
            var selected = new List<ConversationSuggestedActionDto>
            {
                Action("portfolio-reduce-volatility", "如何降低波動率？", "如何降低波動率？請依可用資料比較可行方向並說明取捨。",
                    "How can I reduce volatility?", "how could volatility be reduced? Compare feasible approaches using available data and explain the trade-offs.")
            };
            if (!hasContributions)
                selected.Add(Action("portfolio-risk-contributors", "哪些持股貢獻最多風險？", "哪些持股對風險的貢獻最大？請根據資料分析並說明原因。",
                    "Which holdings contribute the most risk?", "which holdings contribute the most risk? Analyze the data and explain the reasons."));
            if (!UsedTool("run_stress_test"))
                selected.Add(Action("portfolio-stress-test", "遇到股災會跌多少？", "如果遇到股災、升息熊市或科技股修正等壓力情境，可能會跌多少？請依壓力測試說明主要虧損來源，並區分歷史回放與假設情境。",
                    "How much could a market crash cost?", "how much could the portfolio fall in stress scenarios such as a market crash, a rate-hike bear market or a tech correction? Explain the main loss sources and separate historical replays from hypothetical shocks."));
            if (!UsedTool("get_performance_attribution"))
                selected.Add(Action("portfolio-benchmark", "跟大盤比表現如何？", "同期間的報酬跟台股加權含息報酬指數相比如何？請說明超額報酬與主要原因。",
                    "How did it do versus the market?", "how did the return compare with the TAIEX total return index over the same period? Explain the excess return and main drivers."));
            if (!hasTailRisk)
                selected.Add(Action("portfolio-tail-risk", "極端情況下可能虧多少？", "在極端市況下可能虧損多少？請以 95% VaR 與 ES 說明，並註明計算期間與限制。",
                    "How much could I lose in extreme markets?", "how much could be lost in extreme markets? Explain with 95% VaR and ES, including the calculation period and limitations."));
            if (!UsedTool("get_performance_attribution"))
                selected.Add(Action("portfolio-attribution", "哪些持股拖累或貢獻報酬？", "哪些持股與產業對報酬的正負貢獻最大？請根據績效歸因說明。",
                    "Which holdings drove or dragged returns?", "which holdings and sectors contributed most positively and negatively to returns? Explain using performance attribution."));
            if (!UsedTool("get_risk_profile")) selected.Add(profileAction);
            actions = selected;
        }
        // 同名或文字歧義時，不提供無法由既有選擇器唯一辨識的提問。
        return actions.Where(a => a.Prompt.Length <= 2000 && a.PromptEn!.Length <= 2000
            && PortfolioSelection.Named(a.Prompt, portfolios.Select(x => (x.Id, x.Name))).SequenceEqual([portfolio.Id])
            && PortfolioSelection.Named(a.PromptEn!, portfolios.Select(x => (x.Id, x.Name))).SequenceEqual([portfolio.Id])).Take(3).ToArray();
    }

    private async Task<IReadOnlyList<ConversationSuggestedActionDto>> ResearchAsync(Guid userId, AgentRun run, JsonObject output, CancellationToken ct)
    {
        var research = run.ResearchRun;
        if (research is null || research.UserId != userId || !string.IsNullOrWhiteSpace(research.ErrorMessage)
            || research.CitationCount < 1 || string.IsNullOrWhiteSpace(research.Ticker)
            || output["status"]?.GetValue<string>() != "Answered" || output["outputGate"]?["passed"]?.GetValue<bool>() != true
            || output["criticReview"]?["recommendedNextAction"]?.GetValue<string>() != "AcceptAnswer") return [];
        var securities = await db.Securities.AsNoTracking().Where(x => x.IsActive && x.Ticker == research.Ticker).ToListAsync(ct);
        if (securities.Count != 1 || string.Equals(securities[0].AssetType, "ETF", StringComparison.OrdinalIgnoreCase)) return [];
        var ticker = securities[0].Ticker;
        return [
            new("research-operating-risks", "主要營運風險是什麼？", $"接著，針對公司 {ticker} 的研究，主要營運風險是什麼？請附上資料來源。",
                "What are the main operating risks?", $"For company {ticker}, explain the main operating risks with supporting sources."),
            new("research-evidence", "哪些資料支持這個結論？", $"接著，針對公司 {ticker} 的研究，請整理主要結論的支持資料，並指出證據不足之處。",
                "What evidence supports the conclusion?", $"For company {ticker}, summarize the evidence supporting the research conclusions and identify evidence gaps.")
        ];
    }
}
