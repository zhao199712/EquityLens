using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents;

public sealed record ResearchInvestigationInput(
    ResearchAskRequest Request,
    InvestmentResearchRoutingContext? RoutingContext);

public static partial class ResearchInvestigationPlanning
{
    private static readonly string[] FreshnessTerms = ["今天", "昨日", "昨天", "本週", "本月", "近期", "最近", "最新", "為什麼跌", "為甚麼跌", "為什麼漲", "為甚麼漲", "股價異動", "新聞", "today", "yesterday", "latest", "current", "price move", "news"];

    public static bool IsFreshnessSensitive(string question) =>
        FreshnessTerms.Any(x => question.Contains(x, StringComparison.OrdinalIgnoreCase))
        || DatePattern().IsMatch(question);

    [GeneratedRegex(@"(?:\d{4}[/-])?\d{1,2}[/-]\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}

public sealed class ResearchInvestigationWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    private static readonly (string Key, string Type)[] BootstrapSteps =
    [
        (ResearchInvestigationNodeKeys.Validate, ResearchInvestigationNodeTypes.Validate),
        (ResearchInvestigationNodeKeys.DetectIntent, ResearchInvestigationNodeTypes.DetectIntent)
    ];

    public string WorkflowType => AgentWorkflowTypes.ResearchInvestigation;

    public AgentRun CreateRun(Guid userId, Guid researchRunId) =>
        CreateRun(userId, researchRunId, new ResearchAskRequest(string.Empty, string.Empty));

    public AgentRun CreateRun(
        Guid userId,
        Guid researchRunId,
        ResearchAskRequest request,
        InvestmentResearchRoutingContext? routingContext = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, ResearchRunId = researchRunId,
        WorkflowType = WorkflowType, AgentType = AgentTypes.Research, Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new ResearchInvestigationInput(request, routingContext)),
        BlackboardJson = Initial(researchRunId, request, routingContext),
        WorkflowDefinitionJson = Definition(), CreatedAtUtc = DateTime.UtcNow, EnableBlackboardSnapshots = true,
        Nodes = BootstrapSteps.Select(x => new AgentRunNode { Id = Guid.NewGuid(), NodeKey = x.Key, NodeType = x.Type, Status = AgentNodeStatuses.Pending }).ToList()
    };

    public string CreateInitialBlackboardJson(Guid researchRunId) => Initial(researchRunId, new ResearchAskRequest(string.Empty, string.Empty));

    public string CreateInitialBlackboardJson(
        Guid researchRunId,
        ResearchAskRequest request,
        InvestmentResearchRoutingContext? routingContext = null) =>
        Initial(researchRunId, request, routingContext);

    public static ResearchInvestigationInput ParseInput(string inputJson)
    {
        var envelope = JsonSerializer.Deserialize<ResearchInvestigationInput>(inputJson, AgentNodeJson.SerializerOptions);
        if (envelope?.Request is not null) return envelope;
        var legacy = JsonSerializer.Deserialize<ResearchAskRequest>(inputJson, AgentNodeJson.SerializerOptions)
            ?? throw new InvalidOperationException("ResearchInvestigation input is invalid.");
        return new(legacy, null);
    }

    private static string Initial(
        Guid researchRunId,
        ResearchAskRequest request,
        InvestmentResearchRoutingContext? routingContext = null) => new JsonObject
    {
        ["blackboardVersion"] = 0,
        [AgentBlackboardKeys.ResearchRunId] = researchRunId,
        [AgentBlackboardKeys.Ticker] = request.Ticker.Trim().ToUpperInvariant(),
        [AgentBlackboardKeys.Question] = request.Question,
        [AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(request, AgentNodeJson.SerializerOptions),
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.Steps] = new JsonArray()
        ,[AgentBlackboardKeys.MathInputs] = null
        ,[AgentBlackboardKeys.MathResults] = new JsonArray()
        ,[AgentBlackboardKeys.LeadSkill] = routingContext?.LeadSkill
        ,[AgentBlackboardKeys.RoutingContext] = JsonSerializer.SerializeToNode(routingContext, AgentNodeJson.SerializerOptions)
        ,[AgentBlackboardKeys.CapabilityRequestAssessment] = null
        ,[AgentBlackboardKeys.CapabilityRequests] = new JsonArray()
    }.ToJsonString(AgentNodeJson.SerializerOptions);

    private static string Definition()
    {
        var nodes = new JsonArray(BootstrapSteps.Select(x => (JsonNode)new JsonObject { ["id"] = x.Key, ["type"] = x.Type, ["required"] = true }).ToArray());
        var edges = new JsonArray(BootstrapSteps.Zip(BootstrapSteps.Skip(1), (a, b) => (JsonNode)new JsonObject { ["from"] = a.Key, ["to"] = b.Key }).ToArray());
        return new JsonObject { ["workflowType"] = AgentWorkflowTypes.ResearchInvestigation, ["version"] = ResearchInvestigationWorkflow.Version, ["orchestrationMode"] = "DynamicStateful", ["goalStatus"] = "PendingPlanning", ["planningHistory"] = new JsonArray(), ["nodes"] = nodes, ["edges"] = edges }.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}

internal static class ResearchInvestigationBoard
{
    public static JsonObject Parse(AgentRun run) => AgentNodeJson.ParseBlackboard(run.BlackboardJson);
    public static T Required<T>(JsonObject board, string key)
    {
        var node = board[key] ?? throw new AgentNodeException("blackboard_value_missing", AgentNodeErrorCategories.ValidationFailure, $"Blackboard key '{key}' is missing.");
        return node.Deserialize<T>(AgentNodeJson.SerializerOptions)!;
    }
    public static void Commit(AgentNodeExecutionContext context, JsonObject board, object output)
    {
        context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}

public sealed class ValidateResearchRequestNodeHandler(IResearchPreflightService preflight) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.Validate;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest);
        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Question)) throw new AgentNodeException("invalid_research_request", AgentNodeErrorCategories.ValidationFailure, "Ticker and question are required.");
        var result = await preflight.ValidateAskAsync(request, cancellationToken);
        if (!result.IsSuccess) throw new AgentNodeException(result.ErrorCode ?? "preflight_failed", AgentNodeErrorCategories.ValidationFailure, result.ErrorMessage ?? "Research preflight failed.");
        board[AgentBlackboardKeys.Ticker] = result.Value!.Ticker; ResearchInvestigationBoard.Commit(context, board, result.Value);
    }
}

public sealed class DetectResearchIntentNodeHandler(IIntentDetector detector) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.DetectIntent;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var result = detector.Detect(board[AgentBlackboardKeys.Question]!.GetValue<string>());
        board[AgentBlackboardKeys.ResearchIntent] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, result); return Task.CompletedTask;
    }
}

public sealed class PlanResearchRetrievalNodeHandler(IRetrievalPlanner planner, IOptions<RetrievalOptions> options) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.PlanRetrieval;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest);
        var topK = Math.Clamp(request.TopK <= 0 ? options.Value.DefaultTopK : request.TopK, 1, options.Value.MaxTopK);
        var feedback = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>();
        var retrievalQuestion = string.IsNullOrWhiteSpace(feedback) ? request.Question : $"{request.Question}\n使用者要求修正：{feedback}";
        var plan = planner.BuildPlan(retrievalQuestion, request.RetrievalMode, request.DocumentType, topK);
        board[AgentBlackboardKeys.RetrievalPlan] = JsonSerializer.SerializeToNode(plan, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.InitialEvidence] = new JsonArray();
        ResearchInvestigationBoard.Commit(context, board, plan); return Task.CompletedTask;
    }
}

public sealed class RetrieveLocalResearchEvidenceNodeHandler(IDocumentRetriever retriever) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.RetrieveLocal;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var plan = ResearchInvestigationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan);
        IReadOnlyList<RetrievedDocumentChunk> evidence = request.SourcePolicy is SourcePolicy.WebOnly ? [] : await retriever.RetrieveAsync(plan, request.Ticker, cancellationToken);
        board[AgentBlackboardKeys.InitialEvidence] = JsonSerializer.SerializeToNode(evidence, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, new { source = "Local", count = evidence.Count });
    }
}

public sealed record WebCapabilityAssessment(
    string Decision,
    string Reason,
    IReadOnlyList<string> EvidenceGaps,
    string Confidence,
    string Mode,
    string Model,
    int PromptTokens,
    int CompletionTokens);

public interface IWebCapabilityRequestAgent
{
    Task<WebCapabilityAssessment> AssessAsync(
        ResearchAskRequest request,
        IReadOnlyList<RetrievedDocumentChunk> localEvidence,
        CancellationToken cancellationToken = default);
}

public sealed class LlmWebCapabilityRequestAgent(IChatCompletionService chat) : IWebCapabilityRequestAgent
{
    public const string PromptTemplateId = "conference-call-web-capability-request";
    public const int PromptVersion = 1;
    public const int TimeoutSeconds = 10;

    public async Task<WebCapabilityAssessment> AssessAsync(
        ResearchAskRequest request,
        IReadOnlyList<RetrievedDocumentChunk> localEvidence,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            var evidence = localEvidence.Take(8).Select(x => new
            {
                x.Result.DocumentTitle,
                x.Result.DocumentType,
                x.Result.PageNumber,
                content = AgentNodeJson.Trim(x.Result.Content, 500)
            });
            var result = await chat.CompleteAsync(new ChatCompletionRequest(
                """
                You are an evidence-needs node agent. You cannot call tools. You may only propose the supplied capability by returning JSON.
                Decide whether local evidence is insufficient and Web search is materially necessary to answer the question.
                Return {"decision":"Request|NotNeeded","reason":"繁體中文理由","evidenceGaps":["..."],"confidence":"high|medium|low"}.
                Use Traditional Chinese for every string. Request only when Web evidence can plausibly close a specific material gap. Never invent another capability.
                """,
                JsonSerializer.Serialize(new
                {
                    request.Ticker,
                    request.Question,
                    sourcePolicy = request.SourcePolicy.ToString(),
                    availableCapability = new
                    {
                        id = "retrieve-web-research-evidence",
                        description = "Retrieve current Web evidence after Planner and Validator approval."
                    },
                    localEvidence = evidence
                }, AgentNodeJson.SerializerOptions),
                .1,
                900,
                ChatResponseFormat.JsonObject), timeout.Token);
            using var document = JsonDocument.Parse(result.Content);
            var root = document.RootElement;
            var decision = root.GetProperty("decision").GetString();
            var reason = root.GetProperty("reason").GetString();
            var confidence = root.GetProperty("confidence").GetString();
            var gaps = root.GetProperty("evidenceGaps").EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).Take(5).ToList();
            if (decision is not ("Request" or "NotNeeded") || string.IsNullOrWhiteSpace(reason) || confidence is not ("high" or "medium" or "low"))
                throw new JsonException("Capability assessment fields are invalid.");
            if (!reason.Any(x => x is >= '\u3400' and <= '\u9fff')
                || reason.IndexOfAny("发为会这与后国语变从对个们业产当应还进过数资实据".ToCharArray()) >= 0)
                throw new JsonException("Capability assessment reason must use Traditional Chinese.");
            return new(decision, reason, gaps, confidence, "Llm", result.Model, result.PromptTokens, result.CompletionTokens);
        }
        catch (Exception exception) when (exception is JsonException or OperationCanceledException || exception is HttpRequestException)
        {
            var freshness = ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question);
            var requestWeb = request.SourcePolicy == SourcePolicy.LocalThenWeb && (localEvidence.Count == 0 || freshness)
                || request.SourcePolicy == SourcePolicy.Auto && (localEvidence.Count == 0 || freshness);
            return new(
                requestWeb ? "Request" : "NotNeeded",
                requestWeb ? "規則降級判斷本地證據不足或問題具時效性，建議提出 Web 檢索。" : "規則降級判斷可先使用本地證據回答。",
                requestWeb ? ["本地證據不足或缺少時效性資料"] : [],
                "low",
                "DeterministicFallback",
                chat.Model,
                0,
                0);
        }
    }
}

public sealed class EvaluateInitialEvidencePolicyNodeHandler(IWebCapabilityRequestAgent capabilityAgent) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.EvaluateEvidence;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var local = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence);
        var feedback = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>();
        var freshnessSensitive = ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question)
            || (!string.IsNullOrWhiteSpace(feedback) && ResearchInvestigationPlanning.IsFreshnessSensitive(feedback));
        var isPilot = board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>() == "conference-call-takeaways"
            && request.SourcePolicy is SourcePolicy.Auto or SourcePolicy.LocalThenWeb;
        WebCapabilityAssessment? assessment = null;
        AgentToolCall? toolCall = null;
        var started = DateTime.UtcNow;
        if (isPilot)
        {
            toolCall = new AgentToolCall
            {
                Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
                ToolName = "evidenceNeedsAgentLLM", Status = AgentToolCallStatuses.Running,
                ArgumentsJson = AgentNodeJson.Serialize(new { ticker = request.Ticker, localEvidenceCount = local.Count, capability = "retrieve-web-research-evidence", promptTemplateId = LlmWebCapabilityRequestAgent.PromptTemplateId, promptVersion = LlmWebCapabilityRequestAgent.PromptVersion }),
                StartedAtUtc = started
            };
            context.DbContext.AgentToolCalls.Add(toolCall);
            context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallStarted, "Tool evidenceNeedsAgentLLM started.", new { localEvidenceCount = local.Count });
            await context.DbContext.SaveChangesAsync(cancellationToken);
            assessment = await capabilityAgent.AssessAsync(request, local, cancellationToken);
            toolCall.Status = assessment.Mode == "DeterministicFallback" ? AgentToolCallStatuses.Failed : AgentToolCallStatuses.Succeeded;
            toolCall.ResultJson = AgentNodeJson.Serialize(assessment);
            toolCall.ResultPreview = $"{assessment.Decision}: {AgentNodeJson.Trim(assessment.Reason, 140)}";
            toolCall.ErrorMessage = assessment.Mode == "DeterministicFallback" ? "Evidence needs agent used deterministic fallback." : null;
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = (long)(toolCall.CompletedAtUtc.Value - started).TotalMilliseconds;
            context.AddEvent(context.Run, context.Node,
                assessment.Mode == "DeterministicFallback" ? AgentEventTypes.ToolCallFailed : AgentEventTypes.ToolCallCompleted,
                assessment.Mode == "DeterministicFallback" ? "Tool evidenceNeedsAgentLLM fell back to rules." : "Tool evidenceNeedsAgentLLM completed.",
                new { toolCall.DurationMs, assessment.Mode, assessment.Decision });
        }
        var mandatoryWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb;
        var useWeb = mandatoryWeb;
        var requestId = assessment?.Decision == "Request" ? Guid.NewGuid() : (Guid?)null;
        var output = new
        {
            useWeb,
            localCount = local.Count,
            freshnessSensitive,
            sourcePolicy = request.SourcePolicy.ToString(),
            capabilityGate = isPilot,
            assessment
        };
        board[AgentBlackboardKeys.InitialEvidencePolicy] = JsonSerializer.SerializeToNode(output, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.CapabilityRequestAssessment] = JsonSerializer.SerializeToNode(assessment, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.CapabilityRequests] = requestId is null
            ? new JsonArray()
            : new JsonArray(JsonSerializer.SerializeToNode(new
            {
                requestId,
                capabilityId = "retrieve-web-research-evidence",
                requestedByNodeKey = context.Node.NodeKey,
                status = "Pending",
                assessment!.Reason,
                assessment.EvidenceGaps,
                assessment.Confidence,
                createdAtUtc = DateTime.UtcNow,
                reviewedAtUtc = (DateTime?)null,
                reviewReason = (string?)null
            }, AgentNodeJson.SerializerOptions));
        context.Node.InputTokens = assessment?.PromptTokens;
        context.Node.OutputTokens = assessment?.CompletionTokens;
        if (requestId is not null)
            context.AddEvent(context.Run, context.Node, AgentEventTypes.CapabilityRequested, "Node Agent proposed Web research capability.", new { requestId, capability = "retrieve-web-research-evidence", assessment!.Reason, assessment.Mode });
        else if (isPilot)
            context.AddEvent(context.Run, context.Node, AgentEventTypes.CapabilityRequestNotNeeded, "Node Agent determined Web research was not needed.", new { assessment!.Reason, assessment.Mode });
        ResearchInvestigationBoard.Commit(context, board, output);
    }
}

public sealed class RetrieveWebResearchEvidenceNodeHandler(IWebRetriever retriever, IOptions<RetrievalOptions> options) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.RetrieveWeb;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var evidence = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence);
        var useWeb = board[AgentBlackboardKeys.InitialEvidencePolicy]?["useWeb"]?.GetValue<bool>() == true;
        var feedback = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>();
        var query = string.IsNullOrWhiteSpace(feedback) ? request.Question : $"{request.Question} 使用者要求修正：{feedback}";
        if (useWeb) evidence.AddRange(await retriever.RetrieveWebAsync(query, options.Value.WebSearchCandidateCount, options.Value.WebSearchFreshness, cancellationToken));
        board[AgentBlackboardKeys.InitialEvidence] = JsonSerializer.SerializeToNode(evidence, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, new { source = "Web", used = useWeb, totalCount = evidence.Count });
    }
}

public sealed class RankAndSelectResearchEvidenceNodeHandler(IResultReranker reranker, IOptions<RetrievalOptions> options) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.RankEvidence;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var evidence = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence); var intent = ResearchInvestigationBoard.Required<IntentDetectionResult>(board, AgentBlackboardKeys.ResearchIntent); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest);
        var topK = Math.Clamp(request.TopK <= 0 ? options.Value.DefaultTopK : request.TopK, 1, options.Value.MaxTopK);
        var provider = options.Value.RerankProvider;
        var toolName = $"{provider.Trim().ToLowerInvariant()}Rerank";
        var toolCall = new AgentToolCall
        {
            Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
            ToolName = toolName, Status = AgentToolCallStatuses.Running,
            ArgumentsJson = AgentNodeJson.Serialize(new { provider, candidateCount = evidence.Count, topK }),
            StartedAtUtc = DateTime.UtcNow
        };
        context.DbContext.AgentToolCalls.Add(toolCall);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallStarted, $"Tool {toolName} started.", new { provider, candidateCount = evidence.Count, topK });
        await context.DbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var ranked = await reranker.Rank(evidence, intent.Selected, topK, cancellationToken);
            stopwatch.Stop();
            var diagnostics = ranked.RerankDiagnostics;
            var usedFallback = diagnostics?.UsedFallback == true;
            toolCall.Status = usedFallback ? AgentToolCallStatuses.Failed : AgentToolCallStatuses.Succeeded;
            toolCall.ResultJson = diagnostics is null
                ? AgentNodeJson.Serialize(new { Provider = provider, Status = "LocalOnly", UsedFallback = false, CandidateCount = evidence.Count, SelectedCount = ranked.SelectedResults.Count })
                : AgentNodeJson.Serialize(diagnostics);
            toolCall.ResultPreview = usedFallback
                ? $"{diagnostics!.Status}: {diagnostics.FallbackReason} -> local ranking fallback"
                : $"{diagnostics?.Status ?? "LocalOnly"}: {ranked.SelectedResults.Count} selected";
            toolCall.ErrorMessage = usedFallback ? diagnostics!.FallbackReason : null;
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = diagnostics?.DurationMs ?? stopwatch.ElapsedMilliseconds;
            context.AddEvent(context.Run, context.Node, usedFallback ? AgentEventTypes.ToolCallFailed : AgentEventTypes.ToolCallCompleted,
                usedFallback ? $"Tool {toolName} fell back to local ranking." : $"Tool {toolName} completed.",
                new { toolCall.DurationMs, diagnostics?.Status, diagnostics?.FallbackReason, diagnostics?.HttpStatusCode, diagnostics?.PayloadBytes, diagnostics?.CandidateCount, diagnostics?.SelectedCount, usedFallback });

            board[AgentBlackboardKeys.SelectedEvidence] = JsonSerializer.SerializeToNode(ranked, AgentNodeJson.SerializerOptions);
            ResearchInvestigationBoard.Commit(context, board, new { candidateCount = evidence.Count, selectedCount = ranked.SelectedResults.Count, rerankDiagnostics = diagnostics });
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            toolCall.Status = AgentToolCallStatuses.Cancelled; toolCall.ErrorMessage = "Rerank cancelled."; toolCall.CompletedAtUtc = DateTime.UtcNow; toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallFailed, $"Tool {toolName} cancelled.", new { toolCall.DurationMs });
            await context.DbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            toolCall.Status = AgentToolCallStatuses.Failed; toolCall.ErrorMessage = exception.Message; toolCall.CompletedAtUtc = DateTime.UtcNow; toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallFailed, $"Tool {toolName} failed.", new { toolCall.DurationMs, error = exception.Message });
            await context.DbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}

public sealed class DraftResearchAnswerNodeHandler(
    IContextSelector selector,
    IContextFormatter formatter,
    IAnswerGenerator generator,
    EquityLensDbContext db,
    IWorkflowSkillCatalog skillCatalog) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.DraftAnswer;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var plan = ResearchInvestigationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan); var intent = ResearchInvestigationBoard.Required<IntentDetectionResult>(board, AgentBlackboardKeys.ResearchIntent); var ranked = ResearchInvestigationBoard.Required<RankedSelection>(board, AgentBlackboardKeys.SelectedEvidence);
        var selected = selector.Select(ranked, intent.Selected, plan);
        var mathResults = board[AgentBlackboardKeys.MathResults] as JsonArray;
        var feedback = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>();
        var originalAnswer = board[AgentBlackboardKeys.OriginalAnswer]?.GetValue<string>();
        var revisionContext = string.IsNullOrWhiteSpace(feedback) ? string.Empty
            : $"\n\n[User-requested revision]\nOriginal answer:\n{originalAnswer}\n\nUser feedback:\n{feedback}\nRevise the answer to address this feedback. Preserve only claims supported by the supplied evidence and cite them.";
        var formattedContext = formatter.Format(selected.Chunks)
            + (mathResults is { Count: > 0 } ? $"\n\n[Deterministic portfolio/risk mathematics]\n{mathResults.ToJsonString(AgentNodeJson.SerializerOptions)}" : string.Empty)
            + revisionContext;
        var hasEvidence = selected.Chunks.Count > 0 || mathResults is { Count: > 0 };
        var leadSkillId = board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>();
        var leadSkill = string.IsNullOrWhiteSpace(leadSkillId)
            ? null
            : skillCatalog.Skills.SingleOrDefault(x => x.Id == leadSkillId);
        var instructions = leadSkill is { SystemPrompt: not null, PromptTemplateId: not null, PromptVersion: not null }
            ? new AnswerGenerationInstructions(leadSkill.Id, leadSkill.PromptTemplateId, leadSkill.PromptVersion.Value, leadSkill.SystemPrompt)
            : null;
        var answer = !hasEvidence ? new AnswerGenerationResult("目前提供的資料不足以回答此問題。", generator.Model, 0, 0, 0, false, []) : await generator.GenerateAsync(request.Question, formattedContext, selected.RetrievalNote, Math.Clamp(request.Temperature, 0, 1), selected.Chunks.Count, instructions, cancellationToken);
        var citations = selected.Chunks.Select(x => new ResearchCitation(x.Index, x.Chunk.SourceType, x.Chunk.SourceType == CitationSourceType.LocalDocument ? x.Chunk.Result.DocumentChunkId : null, x.Chunk.Result.DocumentId, x.Chunk.Result.DocumentTitle, x.Chunk.Result.DocumentType, x.Chunk.SourceRole, x.Chunk.Result.PageNumber, x.Chunk.Url, x.Chunk.PublishedAt, x.Chunk.RetrievedAt, Trim(x.Chunk.Result.Content), x.Chunk.Result.RelevanceScore)).ToList();
        var status = !hasEvidence ? "InsufficientEvidence" : answer.CitationValidationFailed ? "CitationValidationFailed" : "Answered";
        var citationNodes = JsonSerializer.SerializeToNode(citations.Select(c => new
        {
            citationIndex = c.Index,
            sourceType = c.SourceType.ToString(),
            c.DocumentChunkId,
            c.DocumentId,
            c.Title,
            c.DocumentType,
            c.SourceRole,
            c.PageNumber,
            c.Url,
            c.PublishedAt,
            c.RetrievedAt,
            c.QuoteText,
            c.RelevanceScore
        }), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.Answer] = answer.Answer; board[AgentBlackboardKeys.Citations] = citationNodes; board[AgentBlackboardKeys.Candidates] = JsonSerializer.SerializeToNode(ranked.Decisions.Select((x, i) => new { id = Guid.NewGuid(), documentChunkId = x.Chunk.Result.DocumentChunkId, documentId = x.Chunk.Result.DocumentId, title = x.Chunk.Result.DocumentTitle, documentType = x.Chunk.Result.DocumentType, sourceRole = x.Chunk.SourceRole, pageNumber = x.Chunk.Result.PageNumber, relevanceScore = x.Chunk.Result.RelevanceScore, adjustedScore = x.AdjustedScore, rankBeforeRerank = x.RankBeforeRerank, rankAfterRerank = x.RankAfterRerank, decision = x.Decision, discardReason = x.Reason, contentPreview = Trim(x.Chunk.Result.Content) }), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.ResearchRun] = new JsonObject { ["run"] = new JsonObject { ["id"] = context.Run.ResearchRunId, ["ticker"] = request.Ticker.Trim().ToUpperInvariant(), ["question"] = request.Question, ["status"] = status }, ["answer"] = answer.Answer, ["steps"] = new JsonArray(), ["candidates"] = board[AgentBlackboardKeys.Candidates]!.DeepClone(), ["citations"] = board[AgentBlackboardKeys.Citations]!.DeepClone() };
        var artifact = await db.ResearchRuns.SingleAsync(x => x.Id == context.Run.ResearchRunId, cancellationToken); artifact.Answer = answer.Answer; artifact.Status = "Running"; artifact.Model = answer.Model; artifact.RetrievalMode = plan.Mode; artifact.CitationCount = citations.Count;
        var oldCitations = await db.ResearchRunCitations.Where(x => x.RunId == artifact.Id).ToListAsync(cancellationToken); db.ResearchRunCitations.RemoveRange(oldCitations);
        foreach (var c in citations) db.ResearchRunCitations.Add(new ResearchRunCitation { Id = Guid.NewGuid(), RunId = artifact.Id, CitationIndex = c.Index, SourceType = c.SourceType.ToString(), DocumentChunkId = c.DocumentChunkId, DocumentId = c.DocumentId, Title = c.Title, DocumentType = c.DocumentType, SourceRole = c.SourceRole, PageNumber = c.PageNumber, QuoteText = c.QuoteText, RelevanceScore = c.RelevanceScore });
        context.Node.InputTokens = answer.PromptTokens; context.Node.OutputTokens = answer.CompletionTokens; ResearchInvestigationBoard.Commit(context, board, new { answer.Answer, answer.Model, status, citationCount = citations.Count });
    }
    private static string Trim(string value) => value.Length <= 300 ? value : value[..300] + "...";
}
