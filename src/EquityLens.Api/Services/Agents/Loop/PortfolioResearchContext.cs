using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>由已驗證擁有權及品質的研究結果建立唯讀證據快照，不接受模型提供的數據。</summary>
internal static class PortfolioResearchContext
{
    internal const string Key = "priorResearchEvidence";

    internal static async Task<JsonObject?> LoadAsync(EquityLensDbContext db, Guid owner, Guid? runId, CancellationToken ct)
    {
        if (runId is null) return null;
        var run = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.UserId == owner
            && x.WorkflowType == AgentWorkflowTypes.ResearchAgent && x.Status == AgentRunStatuses.Succeeded, ct)
            ?? throw new AgentNodeException("research_context_forbidden", AgentNodeErrorCategories.PermanentFailure,
                "無法使用指定研究脈絡。", retryable: false);
        var research = await db.ResearchRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == run.ResearchRunId && x.UserId == owner, ct)
            ?? throw new AgentNodeException("research_context_forbidden", AgentNodeErrorCategories.PermanentFailure,
                "無法使用指定研究脈絡。", retryable: false);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var result = new JsonObject { ["agentRunId"] = run.Id.ToString(), ["researchRunId"] = research.Id.ToString(),
            ["ticker"] = research.Ticker, ["question"] = research.Question, ["status"] = "Unavailable",
            ["limitation"] = "前輪研究尚無通過品質檢查的事實；仍可讀取實際持倉，不能補造財報結論。" };
        if (board["outputGate"]?["passed"]?.GetValue<bool>() != true
            || board[AgentBlackboardKeys.ResearchRequest] is null
            || board[AgentBlackboardKeys.Answer]?.GetValue<string>() != research.Answer
            || research.Answer == AgentLoopEngine.Refusal || research.Answer.Length > 6000
            || !ResearchOutputCheck.Check(board).Passed) return result;
        var citations = (board[AgentBlackboardKeys.Citations] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
        if (citations.Length == 0) return result;
        result["status"] = "Verified";
        result["answer"] = research.Answer;
        result["sources"] = new JsonArray(citations.Select(x => (JsonNode)new JsonObject {
            ["citationIndex"] = x["citationIndex"]?.DeepClone(), ["title"] = x["title"]?.DeepClone(),
            ["documentId"] = x["documentId"]?.DeepClone(), ["documentChunkId"] = x["documentChunkId"]?.DeepClone(),
            ["pageNumber"] = x["pageNumber"]?.DeepClone(), ["url"] = x["url"]?.DeepClone(),
            ["sourceType"] = x["sourceType"]?.DeepClone()
        }).ToArray());
        result["verifiedNumbers"] = JsonSerializer.SerializeToNode(ResearchNumberTraceability.VerifiedValues(research.Answer), AgentNodeJson.SerializerOptions);
        result["limitation"] = "財報事實不等於未來報酬或歷史風險的成因。持倉曝險須另讀當前快照；ETF 穿透未知時明示未知。";
        return result;
    }
}
