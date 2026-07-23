using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Chat;

public sealed record ConversationOutcome(
    Guid TurnId,
    Guid AssistantMessageId,
    string Action,
    string Content,
    ConversationRunCardDto? RunCard,
    string Model,
    int PromptTokens,
    int CompletionTokens);

public interface IConversationService
{
    Task<ConversationOutcome> ProcessAsync(Guid userId, Guid sessionId, SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<ConversationRunCardDto?> GetRunCardAsync(Guid userId, Guid sessionId, Guid agentRunId, CancellationToken cancellationToken = default);
}

public sealed class ConversationService(
    EquityLensDbContext db,
    IConversationAgent agent,
    IAgentWorkflowQueryService workflowQueries,
    IAgentRunService agentRuns,
    IAgentWorkflowCatalog workflowCatalog) : IConversationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ConversationOutcome> ProcessAsync(
        Guid userId, Guid sessionId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content)) throw new ConversationException("message_required", "請輸入訊息。");
        if (content.Length > 2000) throw new ConversationException("message_too_long", "訊息不可超過 2000 個字元。");
        if (request.RequestId == Guid.Empty) throw new ConversationException("request_id_required", "requestId 不可為空。");

        var existing = await db.ConversationTurns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.RequestId == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            var existingMessage = await db.ChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ConversationTurnId == existing.Id && x.Role == "assistant", cancellationToken);
            if (existing.Status != "Succeeded" || existingMessage is null)
                throw new ConversationException("turn_in_progress", "此對話請求仍在處理中。");
            var card = existing.AgentRunId is { } runId ? await GetRunCardAsync(userId, sessionId, runId, cancellationToken) : null;
            return new(existing.Id, existingMessage.Id, existing.Action!, existingMessage.Content ?? string.Empty, card,
                existing.Model ?? "unknown", existing.PromptTokens, existing.CompletionTokens);
        }

        var session = await db.ChatSessions.Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken)
            ?? throw new ConversationException("session_not_found", "找不到對話。");
        if (await db.ConversationTurns.AnyAsync(x => x.ChatSessionId == sessionId && x.Status == "Processing", cancellationToken))
            throw new ConversationException("turn_in_progress", "請等待上一則訊息處理完成。");

        var nextSequence = session.Messages.Select(x => x.SequenceNumber ?? -1).DefaultIfEmpty(-1).Max() + 1;
        var turn = new ConversationTurn
        {
            Id = Guid.NewGuid(), ChatSessionId = sessionId, RequestId = request.RequestId,
            InputContextVersion = session.ContextVersion, CreatedAtUtc = DateTime.UtcNow
        };
        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(), ChatSessionId = sessionId, ConversationTurnId = turn.Id,
            Role = "user", Content = content, MessageType = "Text", SequenceNumber = nextSequence
        };
        db.ConversationTurns.Add(turn);
        db.ChatMessages.Add(userMessage);
        await db.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var context = JsonNode.Parse(session.ContextJson) as JsonObject ?? new JsonObject();
            var history = session.Messages.OrderBy(x => x.SequenceNumber).ToList();
            var decision = await agent.DecideAsync(new(content, context, history), cancellationToken);
            turn.Action = decision.Action;
            turn.ReasonCode = decision.ReasonCode;
            turn.StandaloneQuery = decision.StandaloneQuery;
            turn.Model = decision.Model;
            turn.Provider = decision.Provider;
            turn.PromptTokens = decision.PromptTokens;
            turn.CompletionTokens = decision.CompletionTokens;

            AgentWorkflowQueryCreatedResponse? created = null;
            if (decision.Action == ConversationActions.RouteWorkflow)
            {
                created = await workflowQueries.CreateAsync(userId, new(decision.StandaloneQuery!), cancellationToken);
                turn.AgentRunId = created.AgentRunId;
                turn.ResearchRunId = created.ResearchRunId;
            }
            else if (decision.Action == ConversationActions.RevisePreviousRun)
            {
                var parentId = ReadGuid(context, "lastAgentRunId")
                    ?? throw new ConversationException("previous_run_required", "找不到可修正的上一份研究結果。");
                var parent = await db.AgentRuns.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == parentId && x.UserId == userId, cancellationToken);
                if (parent is null || parent.Status != AgentRunStatuses.Succeeded || parent.ResearchRunId is null)
                    throw new ConversationException("previous_run_not_ready", "上一份研究尚未完成或不支援修正。");
                var feedback = await agentRuns.SubmitFeedbackAsync(parentId, userId,
                    new(Guid.NewGuid(), "NeedsCorrection", decision.StandaloneQuery), cancellationToken);
                if (feedback.FollowUpAgentRun is null)
                    throw new ConversationException("revision_not_created", "無法建立修正 Run。");
                turn.AgentRunId = feedback.FollowUpAgentRun.Id;
                turn.ResearchRunId = feedback.FollowUpResearchRunId;
            }

            var nextContext = decision.Action == ConversationActions.ResetContext
                ? new JsonObject()
                : MergeContext(context, decision.ContextPatch);
            if (!string.IsNullOrWhiteSpace(decision.Summary)) nextContext["summary"] = decision.Summary;
            if (turn.AgentRunId is { } agentRunId) nextContext["lastAgentRunId"] = agentRunId.ToString();
            if (turn.ResearchRunId is { } researchRunId) nextContext["lastResearchRunId"] = researchRunId.ToString();

            session.ContextVersion++;
            session.ContextJson = nextContext.ToJsonString(Json);
            session.UpdatedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = content.Length > 50 ? content[..50] + "..." : content;

            var assistant = new ChatMessage
            {
                Id = Guid.NewGuid(), ChatSessionId = sessionId, ConversationTurnId = turn.Id,
                AgentRunId = turn.AgentRunId, Role = "assistant", Content = decision.Response,
                MessageType = turn.AgentRunId.HasValue ? "AgentRun" : "Text", SequenceNumber = nextSequence + 1
            };
            db.ChatMessages.Add(assistant);
            turn.Status = "Succeeded";
            turn.OutputContextVersion = session.ContextVersion;
            turn.CompletedAtUtc = DateTime.UtcNow;
            turn.DurationMs = stopwatch.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            var card = turn.AgentRunId is { } id ? await GetRunCardAsync(userId, sessionId, id, cancellationToken) : null;
            return new(turn.Id, assistant.Id, decision.Action, decision.Response, card,
                decision.Model, decision.PromptTokens, decision.CompletionTokens);
        }
        catch (Exception exception)
        {
            turn.Status = "Failed";
            turn.ErrorMessage = exception.Message;
            turn.CompletedAtUtc = DateTime.UtcNow;
            turn.DurationMs = stopwatch.ElapsedMilliseconds;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ConversationRunCardDto?> GetRunCardAsync(
        Guid userId, Guid sessionId, Guid agentRunId, CancellationToken cancellationToken = default)
    {
        var linked = await db.ChatMessages.AsNoTracking()
            .AnyAsync(x => x.ChatSessionId == sessionId && x.AgentRunId == agentRunId
                && x.ChatSession.UserId == userId, cancellationToken);
        if (!linked) return null;
        var run = await db.AgentRuns.AsNoTracking().Include(x => x.Nodes).Include(x => x.ResearchRun)
            .FirstOrDefaultAsync(x => x.Id == agentRunId && x.UserId == userId, cancellationToken);
        if (run is null) return null;
        var current = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Running).OrderBy(x => x.StartedAtUtc).FirstOrDefault()
            ?? run.Nodes.Where(x => x.Status == AgentNodeStatuses.Pending).OrderBy(x => x.NodeKey).FirstOrDefault();
        var currentContract = current is null ? null : workflowCatalog.GetNode(current.NodeType).Contract;
        var finalAnswer = !string.IsNullOrWhiteSpace(run.ResearchRun?.Answer)
            ? run.ResearchRun.Answer
            : ExtractFinalAnswer(run.OutputJson);
        return new(run.Id, run.ResearchRunId, run.WorkflowType, run.Status, currentContract?.Stage, currentContract?.DisplayName,
            run.Nodes.Count(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped),
            run.Nodes.Count, finalAnswer, run.ErrorMessage);
    }

    private static JsonObject MergeContext(JsonObject current, JsonObject patch)
    {
        var result = current.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var item in patch)
            if (item.Key is not ("lastAgentRunId" or "lastResearchRunId"))
                result[item.Key] = item.Value?.DeepClone();
        return result;
    }

    private static Guid? ReadGuid(JsonObject context, string key) =>
        Guid.TryParse(context[key]?.GetValue<string>(), out var value) ? value : null;

    private static string? ExtractFinalAnswer(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return null;
        try
        {
            var node = JsonNode.Parse(outputJson);
            return Find(node, ["finalAnswer", "answer", "revisedAnswer", "summary"]);
        }
        catch (JsonException) { return null; }
    }

    private static string? Find(JsonNode? node, IReadOnlyList<string> names)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in names)
                if (obj.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value is JsonValue value
                    && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
            foreach (var value in obj.Select(x => x.Value))
                if (Find(value, names) is { } found) return found;
        }
        if (node is JsonArray array)
            foreach (var value in array)
                if (Find(value, names) is { } found) return found;
        return null;
    }
}

public sealed class ConversationException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
