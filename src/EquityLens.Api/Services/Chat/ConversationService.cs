using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    int CompletionTokens,
    IReadOnlyList<string>? Choices = null);

public interface IConversationService
{
    Task<ConversationOutcome> ProcessAsync(Guid userId, Guid sessionId, SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<ConversationRunCardDto?> GetRunCardAsync(Guid userId, Guid sessionId, Guid agentRunId, CancellationToken cancellationToken = default);
}

public sealed partial class ConversationService(
    EquityLensDbContext db,
    IConversationAgent agent,
    IAgentWorkflowQueryService workflowQueries,
    IAgentRunService agentRuns,
    IAgentWorkflowCatalog workflowCatalog,
    ConversationSuggestedActionProvider? suggestions = null) : IConversationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly TimeSpan StaleTurnTimeout = TimeSpan.FromMinutes(5);

    public async Task<ConversationOutcome> ProcessAsync(
        Guid userId, Guid sessionId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content)) throw new ConversationException("message_required", "請輸入訊息。");
        if (content.Length > 2000) throw new ConversationException("message_too_long", "訊息不可超過 2000 個字元。");
        if (request.RequestId == Guid.Empty) throw new ConversationException("request_id_required", "requestId 不可為空。");

        if (!await db.ChatSessions.AnyAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken))
            throw new ConversationException("session_not_found", "找不到對話。");
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
        // 中斷或並行請求可能留下永遠未完成的 turn；逾時後不再鎖住整段對話。
        var staleBefore = DateTime.UtcNow - StaleTurnTimeout;
        if (await db.ConversationTurns.AnyAsync(x => x.ChatSessionId == sessionId && x.Status == "Processing" && x.CreatedAtUtc > staleBefore, cancellationToken))
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
            var resetAfter = context["resetAfterSequence"]?.GetValue<int>() ?? -1;
            var history = session.Messages.Where(x => (x.SequenceNumber ?? -1) > resetAfter).OrderBy(x => x.SequenceNumber).ToList();
            var portfolios = await db.Portfolios.AsNoTracking()
                .Where(x => x.OwnerUserId == userId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new ConversationPortfolioOption(x.Id, x.Name))
                .ToListAsync(cancellationToken);
            var priorActive = ReadGuid(context, "activePortfolioId");
            if (!portfolios.Any(p => p.Id == priorActive)) context.Remove("activePortfolioId");
            var named = PortfolioSelection.NamedOrPartial(content, portfolios.Select(p => (p.Id, p.Name))).ToArray();
            var optionList = context["portfolioOptions"] as JsonArray;
            var choiceIndex = optionList is null ? null : ChoiceIndex(content, optionList.Count);
            var choice = new { Success = choiceIndex is not null };
            if (named.Length == 0 && choiceIndex is int index
                && Guid.TryParse(optionList![index - 1]?.ToString(), out var optionId) && portfolios.Any(p => p.Id == optionId))
                named = [optionId];
            var priorPendingQuestion = context["pendingTask"]?["question"]?.GetValue<string>();
            context.Remove("pendingChoices");
            if (named.Length == 1) context["activePortfolioId"] = named[0].ToString();
            else if (named.Length > 1) context.Remove("activePortfolioId");
            var activePortfolioId = ReadGuid(context, "activePortfolioId");
            var selectionOnly = named.Length == 1 && (choice.Success || portfolios.Any(p => p.Id == named[0] && IsSelectionOnly(content, p)));
            var pendingQuestion = context["pendingTask"]?["question"]?.GetValue<string>();
            var question = selectionOnly && !string.IsNullOrWhiteSpace(pendingQuestion)
                ? PendingQuestion(pendingQuestion, content) : content;
            var inventory = Regex.IsMatch(content, @"^(?:請問)?我(?:有幾個|有哪些|的)投資組合[？?。\s]*$");
            var entry = inventory
                ? LocalDecision(ConversationActions.DirectResponse, portfolios.Count == 0 ? "目前沒有投資組合，請先到「投資組合」頁面建立。" : PortfolioOptionsText(portfolios))
                : selectionOnly && string.IsNullOrWhiteSpace(pendingQuestion)
                    ? LocalDecision(ConversationActions.DirectResponse, $"目前投組：{portfolios.Single(p => p.Id == activePortfolioId).Name}。請告訴我要分析的問題。")
                    : named.Length > 0 && activePortfolioId != priorActive ? null : await EntryDecisionAsync(userId, content, context, cancellationToken);
            if (inventory || selectionOnly || named.Length > 0 && activePortfolioId != priorActive)
            { context.Remove("lastAgentRunId"); if (activePortfolioId != priorActive) context.Remove("lastTask"); }
            if (inventory) context["portfolioOptions"] = JsonSerializer.SerializeToNode(portfolios.Select(p => p.Id), Json);
            var inputContext = context.DeepClone().AsObject();
            await EnrichContextAsync(userId, sessionId, resetAfter, activePortfolioId, question, inputContext, cancellationToken);
            var decision = entry ?? await agent.DecideAsync(new(question, inputContext, history, portfolios), cancellationToken);
            if (decision.Action == ConversationActions.RouteWorkflow && decision.Route is { } classified)
            {
                // 對話先分類；上一個 run 只提供同角色背景，不替目前問題選角色。
                var parent = await OwnedCompletedRunAsync(userId, ReadGuid(context, "lastAgentRunId"), cancellationToken);
                var parentId = parent?.WorkflowType == classified.WorkflowType ? parent.Id : (Guid?)null;
                decision = decision with { Route = classified with { RoutingContext = classified.RoutingContext with {
                    ParentAgentRunId = parentId,
                    ResearchContextRunId = classified.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis
                        ? ReadGuid(inputContext, "researchContextRunId") : null
                } } };
            }
            turn.Action = decision.Action;
            turn.ReasonCode = decision.ReasonCode;
            turn.StandaloneQuery = decision.StandaloneQuery;
            turn.Model = decision.Model;
            turn.Provider = decision.Provider;
            turn.PromptTokens = decision.PromptTokens;
            turn.CompletionTokens = decision.CompletionTokens;

            AgentWorkflowQueryCreatedResponse? created = null;
            string? routingFailure = null;
            var routingRejected = false;
            try
            {
                if (decision.Action == ConversationActions.RouteWorkflow)
                {
                    created = await workflowQueries.CreateForConversationAsync(userId,
                        new(decision.StandaloneQuery!, decision.Route?.RoutingContext.ParentAgentRunId), decision.Route,
                        content, activePortfolioId, cancellationToken);
                    turn.AgentRunId = created.AgentRunId;
                    turn.ResearchRunId = created.ResearchRunId;
                }
                else if (decision.Action == ConversationActions.RevisePreviousRun)
                {
                    var parentId = ReadGuid(context, "lastAgentRunId")
                        ?? throw new ConversationException("previous_run_required", "找不到可修正的上一份研究結果。");
                    var parent = await db.AgentRuns.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == parentId && x.UserId == userId, cancellationToken);
                    if (parent is null || parent.Status != AgentRunStatuses.Succeeded || (parent.ResearchRunId is null && parent.WorkflowType != AgentWorkflowTypes.PortfolioDiagnosis))
                        throw new ConversationException("previous_run_not_ready", "上一份研究尚未完成或不支援修正。");
                    if (parent.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis)
                    {
                        created = await workflowQueries.CreateForConversationAsync(userId, new(decision.StandaloneQuery!, parentId),
                            null, content, activePortfolioId, cancellationToken);
                        turn.AgentRunId = created.AgentRunId;
                        turn.ResearchRunId = null;
                    }
                    else
                    {
                        var feedback = await agentRuns.SubmitFeedbackAsync(parentId, userId,
                            new(Guid.NewGuid(), "NeedsCorrection", decision.StandaloneQuery), cancellationToken);
                        if (feedback.FollowUpAgentRun is null)
                            throw new ConversationException("revision_not_created", "無法建立修正 Run。");
                        turn.AgentRunId = feedback.FollowUpAgentRun.Id;
                        turn.ResearchRunId = feedback.FollowUpResearchRunId;
                    }
                }
            }
            catch (AgentWorkflowQueryException exception)
            {
                routingRejected = exception.Code is "portfolio_forbidden" or "routing_rejected";
                routingFailure = exception.Code == "portfolio_required"
                    ? portfolios.Count == 0 ? "目前沒有可供診斷的投資組合。請先到「投資組合」頁面建立投資組合後再詢問。" : PortfolioOptionsText(portfolios)
                    : exception.Message;
                if (exception.Code == "portfolio_required" && portfolios.Count > 0)
                    context["portfolioOptions"] = JsonSerializer.SerializeToNode(portfolios.Select(p => p.Id), Json);
            }

            var action = routingFailure is null ? decision.Action : routingRejected ? ConversationActions.RejectUnsafe : ConversationActions.AskClarification;
            var response = routingFailure ?? decision.Response;
            // 模型有時以一般回覆反問「要看哪一個投組」；視同釐清，才能保存原問題，使用者選定後接續執行。
            if (!inventory && action == ConversationActions.DirectResponse && portfolios.Count > 1
                && portfolios.Count(p => response.Contains(p.Name, StringComparison.OrdinalIgnoreCase)) >= 2
                && Regex.IsMatch(response, @"[？?]|請(?:選擇|指定|問|告訴)|哪一個|哪個"))
            {
                action = ConversationActions.AskClarification;
                turn.Action = action;
            }
            // 反問投組時提供可點選項，並以同一順序記錄序號，讓「1」「第一個」「A」都能對應。
            IReadOnlyList<string>? choices = null;
            var asksPortfolio = portfolios.Count > 1 && (inventory || response.StartsWith("請選擇投資組合")
                || action == ConversationActions.AskClarification && portfolios.Count(p => response.Contains(p.Name, StringComparison.OrdinalIgnoreCase)) >= 2);
            if (asksPortfolio)
            {
                // 依回覆文字中出現的順序編號，使「第一個」指的就是使用者看到的第一個。
                var ordered = portfolios.OrderBy(p => response.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) is var i and >= 0 ? i : int.MaxValue).ToArray();
                context["portfolioOptions"] = JsonSerializer.SerializeToNode(ordered.Select(p => p.Id), Json);
                choices = ordered.Select(p => p.Name).ToArray();
            }
            turn.Action = action;

            var nextContext = decision.Action == ConversationActions.ResetContext
                ? new JsonObject()
                : MergeContext(context, decision.ContextPatch);
            if (!string.IsNullOrWhiteSpace(decision.Summary)) nextContext["summary"] = decision.Summary;
            if (turn.AgentRunId is { } agentRunId) nextContext["lastAgentRunId"] = agentRunId.ToString();
            if (turn.ResearchRunId is { } researchRunId)
            {
                nextContext["lastResearchRunId"] = researchRunId.ToString();
                nextContext["lastResearchAgentRunId"] = turn.AgentRunId?.ToString();
            }
            if (action == ConversationActions.ResetContext)
                nextContext["resetAfterSequence"] = nextSequence + 1;
            else if (action == ConversationActions.AskClarification)
            {
                nextContext["pendingTask"] = new JsonObject { ["question"] = decision.StandaloneQuery ?? question,
                    ["missing"] = decision.ContextPatch["pendingTask"]?["missing"]?.DeepClone(),
                    ["parameters"] = decision.ContextPatch["pendingTask"]?["parameters"]?.DeepClone() };
                // 使用者補充條件但仍缺投組時，先確認已記下，避免看起來像沒有讀到補充內容。
                if (!string.IsNullOrWhiteSpace(priorPendingQuestion) && !selectionOnly
                    && (decision.StandaloneQuery ?? question) != priorPendingQuestion && !response.StartsWith("好的"))
                    response = "好的，已記下這個條件。" + response;
            }
            else if (turn.AgentRunId is not null)
            {
                nextContext.Remove("pendingTask");
                nextContext["lastTask"] = new JsonObject { ["question"] = decision.StandaloneQuery ?? question,
                    ["workflow"] = decision.Route?.WorkflowType,
                    ["scenarios"] = JsonSerializer.SerializeToNode(decision.Route?.RoutingContext.Scenarios, Json) };
            }

            if (choices is not null) nextContext["pendingChoices"] = JsonSerializer.SerializeToNode(choices, Json);
            else nextContext.Remove("pendingChoices");
            session.ContextVersion++;
            session.ContextJson = nextContext.ToJsonString(Json);
            session.UpdatedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = content.Length > 50 ? content[..50] + "..." : content;

            var assistant = new ChatMessage
            {
                Id = Guid.NewGuid(), ChatSessionId = sessionId, ConversationTurnId = turn.Id,
                AgentRunId = turn.AgentRunId, Role = "assistant", Content = response,
                MessageType = turn.AgentRunId.HasValue ? "AgentRun" : "Text", SequenceNumber = nextSequence + 1
            };
            db.ChatMessages.Add(assistant);
            turn.Status = "Succeeded";
            turn.OutputContextVersion = session.ContextVersion;
            turn.CompletedAtUtc = DateTime.UtcNow;
            turn.DurationMs = stopwatch.ElapsedMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            var card = turn.AgentRunId is { } id ? await GetRunCardAsync(userId, sessionId, id, cancellationToken) : null;
            return new(turn.Id, assistant.Id, action, response, card,
                decision.Model, decision.PromptTokens, decision.CompletionTokens, choices);
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
        var waiting = run.Status is AgentRunStatuses.WaitingForFeedback or AgentRunStatuses.WaitingForApproval;
        var current = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Running).OrderBy(x => x.StartedAtUtc).FirstOrDefault()
            ?? run.Nodes.Where(x => x.Status == AgentNodeStatuses.Pending).OrderBy(x => x.NodeKey).FirstOrDefault();
        var currentContract = current is null ? null : workflowCatalog.Nodes.SingleOrDefault(x => x.NodeType == current.NodeType)?.Contract;
        var finalAnswer = !string.IsNullOrWhiteSpace(run.ResearchRun?.Answer)
            ? run.ResearchRun.Answer
            : ExtractFinalAnswer(run.OutputJson);
        var issues = AgentNodeJson.ParseBlackboard(run.BlackboardJson)["portfolioQualityGate"]?["blockingIssues"]?.Deserialize<List<string>>(AgentNodeJson.SerializerOptions);
        return new(run.Id, run.ResearchRunId, run.WorkflowType, run.Status, waiting ? "Approval" : currentContract?.Stage, waiting ? "等待人工審閱" : currentContract?.DisplayName,
            run.Nodes.Count(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped),
            run.Nodes.Count, finalAnswer, run.ErrorMessage, issues,
            suggestions is null ? [] : await suggestions.ForRunAsync(userId, sessionId, run, finalAnswer, cancellationToken));
    }

    private async Task<ConversationAgentDecision?> EntryDecisionAsync(Guid userId, string content, JsonObject context, CancellationToken ct)
    {
        static ConversationAgentDecision Local(string action, string text, string? query = null, InvestmentResearchRouteDecision? route = null) =>
            new(action, text, query, "entry", "high", new(), "", "", "", 0, 0, 0, route);
        if (Regex.IsMatch(content, @"^(?:請)?(?:忘記前面|重新開始|重置對話|reset)(?:[。！\s]|$)", RegexOptions.IgnoreCase))
            return Local(ConversationActions.ResetContext, "已重置對話，請輸入新的問題。");
        var parentId = ReadGuid(context, "lastAgentRunId");
        if (parentId is null) return null;
        var parent = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId && x.UserId == userId, ct);
        if (parent is null) return null;
        if (Regex.IsMatch(content, @"(?:上一|剛剛|前一|你的|答案).*(?:錯|遺漏|補充|修正)|^(?:請)?修正"))
            return Local(ConversationActions.RevisePreviousRun, "會依你的回饋修訂上一份分析。", content);
        return null;
    }

    private static ConversationAgentDecision LocalDecision(string action, string response) =>
        new(action, response, null, "portfolio_context", "high", new(), "", "", "", 0, 0, 0);

    private static string PortfolioOptionsText(IReadOnlyList<ConversationPortfolioOption> portfolios) =>
        "請選擇投資組合（回覆完整名稱或序號）：" + string.Join("；", portfolios.Select((p, i) => $"{i + 1}. {p.Name}"));

    private static JsonObject MergeContext(JsonObject current, JsonObject patch)
    {
        var result = current.DeepClone() as JsonObject ?? new JsonObject();
        foreach (var item in patch)
            if (item.Key is not ("lastAgentRunId" or "lastResearchRunId" or "activePortfolioId" or "portfolioOptions" or "pendingTask" or "lastTask" or "lastResearchAgentRunId" or "resetAfterSequence" or "researchContextRunId" or "pendingChoices"))
                result[item.Key] = item.Value?.DeepClone();
        return result;
    }

    /// <summary>解析「1」「第一個」「A」「前者／後者」等選項指稱；超出範圍回傳 null。</summary>
    internal static int? ChoiceIndex(string content, int count)
    {
        var text = content.Trim().TrimEnd('。', '！', '!', '.', ' ');
        if (count <= 0) return null;
        if (Regex.IsMatch(text, @"^(?:選|我選|就)?前者$")) return 1;
        if (Regex.IsMatch(text, @"^(?:選|我選|就)?後者$")) return count;
        var match = Regex.Match(text, @"^(?:選|我選|就)?(?:第)?(?<n>[1-9][0-9]?|[一二三四五六七八九十]|[A-Ja-j])(?:個|項|號|組)?(?:投組|投資組合)?$");
        if (!match.Success) return null;
        var token = match.Groups["n"].Value;
        var index = char.IsAsciiLetter(token[0]) ? char.ToUpperInvariant(token[0]) - 'A' + 1
            : char.IsAsciiDigit(token[0]) ? int.Parse(token) : "一二三四五六七八九十".IndexOf(token[0]) + 1;
        return index >= 1 && index <= count ? index : null;
    }

    private static Guid? ReadGuid(JsonObject context, string key) =>
        Guid.TryParse(context[key]?.GetValue<string>(), out var value) ? value : null;

    private static string? ExtractFinalAnswer(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return null;
        try
        {
            var node = JsonNode.Parse(outputJson);
            var summary = Find(node, ["finalAnswer", "answer", "revisedAnswer", "summary"]);
            var interpretation = node?["interpretation"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(interpretation) || interpretation == summary) return summary;
            // 摘要與解讀都會附上風險偏好等固定段落；合併時不重複顯示同一段。
            var extra = interpretation.Split("\n\n").Where(part => summary is null || !summary.Contains(part.Trim(), StringComparison.Ordinal));
            var rest = string.Join("\n\n", extra).Trim();
            return string.IsNullOrWhiteSpace(rest) ? summary : summary + "\n\n" + rest;
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
