using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Services.Chat;

public sealed partial class ConversationSuggestedActionProvider
{
    internal const string RankingKey = "conversationSuggestedActionRanking";

    private Task<IReadOnlyList<ConversationSuggestedActionDto>> CandidatesAsync(
        AgentRun run, JsonObject board, JsonObject output, CancellationToken ct) => run.WorkflowType switch
    {
        AgentWorkflowTypes.PortfolioDiagnosis => PortfolioAsync(run.UserId, run, board, output, ct),
        AgentWorkflowTypes.ResearchAgent => ResearchAsync(run.UserId, run, output, ct),
        _ => Task.FromResult<IReadOnlyList<ConversationSuggestedActionDto>>([])
    };

    internal static IReadOnlyList<ConversationSuggestedActionDto> ApplySavedRanking(
        IReadOnlyList<ConversationSuggestedActionDto> candidates, JsonNode? ranking)
    {
        if (ranking is not JsonObject saved || saved["ids"] is not JsonArray ids) return candidates.Take(3).ToArray();
        // 重新檢查規則後只保留仍有效的候選；模型與舊資料不能帶入自訂提問。
        return ids.OfType<JsonValue>().Select(x => x.ToString()).Distinct(StringComparer.Ordinal)
            .Select(id => candidates.FirstOrDefault(x => x.Id == id)).OfType<ConversationSuggestedActionDto>().Take(3).ToArray();
    }

    /// <summary>在 finalize 的租約保護範圍內保存一次排序；重新讀取卡片或恢復執行不重複付費。</summary>
    public async Task PrepareAsync(AgentNodeExecutionContext context, string answer, CancellationToken ct = default)
    {
        if (ranker is null || string.IsNullOrWhiteSpace(answer) || !string.IsNullOrWhiteSpace(context.Run.ErrorMessage)) return;
        var run = context.Run;
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        if (board.ContainsKey(RankingKey)) return;
        var output = JsonNode.Parse(run.OutputJson ?? "{}") as JsonObject;
        if (output is null || output["errorCode"] is not null) return;
        var candidates = await CandidatesAsync(run, board, output, ct);
        if (candidates.Count == 0) return;
        // 先保存固定候選，呼叫中斷後恢復時沿用；外部模型呼叫不宣稱 exactly-once。
        var fallback = new SuggestedActionRanking(candidates.Take(3).Select(x => x.Id).ToArray(), "rules", "interrupted");
        board[RankingKey] = JsonSerializer.SerializeToNode(fallback, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        await context.DbContext.SaveChangesAsync(ct);
        var question = board[AgentBlackboardKeys.RoutingContext]?["objective"]?.ToString()
            ?? board[AgentBlackboardKeys.Question]?.ToString() ?? "";
        var timer = Stopwatch.StartNew();
        var ranked = await ranker.RankAsync(question, answer, candidates, ct);
        ct.ThrowIfCancellationRequested();
        board[RankingKey] = JsonSerializer.SerializeToNode(ranked, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.InputTokens = ranked.PromptTokens;
        context.Node.OutputTokens = ranked.CompletionTokens;
        context.Node.EstimatedCostUsd = ranker.Cost(ranked.PromptTokens, ranked.CompletionTokens);
        context.DbContext.AgentLlmTurns.Add(new AgentLlmTurn {
            Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = context.Node.Id,
            ExecutionAttempt = run.ExecutionAttempt, Turn = 1, Mode = "SuggestedActions",
            Provider = ranker.Provider, Model = ranked.Model ?? "", PromptTemplateId = "conversation-suggested-actions",
            Action = ranked.Source == "model" ? "select_suggestions" : "suggestions_fallback",
            ActionJson = board[RankingKey]!.ToJsonString(), PromptTokens = ranked.PromptTokens,
            CompletionTokens = ranked.CompletionTokens, EstimatedCostUsd = context.Node.EstimatedCostUsd.Value, ErrorMessage = ranked.Failure,
            LatencyMs = timer.ElapsedMilliseconds, CreatedAtUtc = DateTime.UtcNow
        });
        context.AddEvent(run, context.Node, "SuggestedActionsRanked", "追問候選排序已保存。",
            new { ranked.Source, ranked.Ids, ranked.Failure, ranked.Model, ranked.PromptTokens, ranked.CompletionTokens });
        await context.DbContext.SaveChangesAsync(ct);
    }
}
