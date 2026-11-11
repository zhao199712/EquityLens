using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Chat;

public sealed partial class ConversationService
{
    private static bool IsSelectionOnly(string content, ConversationPortfolioOption portfolio) =>
        content.Trim('。', '！', ' ', '？', '?') == portfolio.Id.ToString()
        || Regex.IsMatch(content, @"^(?:請|接下來|先|目前|改|換|切換|看|選擇|選定|選|使用|成|到|為|用|\s)*"
            + Regex.Escape(portfolio.Name) + @"[。！\s]*$");

    private static string PendingQuestion(string original, string supplement)
    {
        var result = $"{original}\n使用者補充：{supplement}";
        // 不截斷使用者限制；超長時讓既有問題長度檢查要求重新表述。
        return result;
    }

    private Task<AgentRun?> OwnedCompletedRunAsync(Guid userId, Guid? id, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId
            && x.Status == AgentRunStatuses.Succeeded, ct);

    private async Task EnrichContextAsync(Guid userId, Guid sessionId, int resetAfter, Guid? portfolioId, string question, JsonObject context, CancellationToken ct)
    {
        var last = await OwnedCompletedRunAsync(userId, ReadGuid(context, "lastAgentRunId"), ct);
        if (last?.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis)
        {
            var input = PortfolioDiagnosisWorkflowDefinitionProvider.ParseInput(last.InputJson);
            if (input.PortfolioId == portfolioId)
            {
                context["previousScenarios"] = JsonSerializer.SerializeToNode(input.RoutingContext?.Scenarios, Json);
                context["lastTask"] = new JsonObject { ["question"] = input.RoutingContext?.Objective,
                    ["workflow"] = last.WorkflowType, ["portfolioId"] = input.PortfolioId.ToString() };
            }
        }
        var researchId = ReadGuid(context, "lastResearchAgentRunId")
            ?? (last?.WorkflowType == AgentWorkflowTypes.ResearchAgent ? last.Id : (Guid?)null);
        var research = await OwnedCompletedRunAsync(userId, researchId, ct);
        // 最近一次研究失敗（如標的缺文件）時，不應讓同一段對話先前成功的研究證據消失。
        if (research?.WorkflowType != AgentWorkflowTypes.ResearchAgent)
        {
            var fallbackId = await db.ChatMessages.AsNoTracking()
                .Where(m => m.ChatSessionId == sessionId && m.ChatSession.UserId == userId && (m.SequenceNumber ?? -1) > resetAfter
                    && m.AgentRunId != null && m.AgentRun!.UserId == userId
                    && m.AgentRun.WorkflowType == AgentWorkflowTypes.ResearchAgent && m.AgentRun.Status == AgentRunStatuses.Succeeded)
                .OrderByDescending(m => m.SequenceNumber).Select(m => m.AgentRunId).FirstOrDefaultAsync(ct);
            research = await OwnedCompletedRunAsync(userId, fallbackId, ct);
        }
        string? ticker = null;
        if (research?.WorkflowType == AgentWorkflowTypes.ResearchAgent && research.ResearchRunId is { } rid)
        {
            var artifact = await db.ResearchRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rid && x.UserId == userId, ct);
            if (artifact is not null)
            {
                ticker = artifact.Ticker;
                context["researchContextRunId"] = research.Id.ToString();
                context["recentResearch"] = new JsonObject { ["ticker"] = ticker, ["question"] = artifact.Question,
                    ["status"] = artifact.Status, ["note"] = "僅供辨識前輪主題；研究事實須由投組執行時重新驗證來源。" };
            }
        }
        var holdings = await db.PortfolioHoldings.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.Portfolio.OwnerUserId == userId && x.Portfolio.IsActive)
            .Select(x => new { x.Security.Ticker, x.Security.Name }).ToListAsync(ct);
        context["portfolioSecurities"] = JsonSerializer.SerializeToNode(holdings, Json);
        var mentioned = question + " " + context["pendingTask"]?["question"] + " " + context["lastTask"]?["question"];
        var securities = await db.Securities.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Ticker, x.Name }).ToListAsync(ct);
        context["tickerCandidates"] = JsonSerializer.SerializeToNode(securities.Where(x => x.Ticker == ticker
            || holdings.Any(h => h.Ticker == x.Ticker) || mentioned.Contains(x.Ticker, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(x.Name) && mentioned.Contains(x.Name, StringComparison.OrdinalIgnoreCase))).Take(30), Json);
    }
}
