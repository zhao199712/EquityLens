using System.Text.Json;
using System.Text.Json.Nodes;
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

    public AgentRun CreateRun(Guid userId, Guid researchRunId, ResearchAskRequest request) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, ResearchRunId = researchRunId,
        WorkflowType = WorkflowType, AgentType = AgentTypes.Research, Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(request), BlackboardJson = Initial(researchRunId, request),
        WorkflowDefinitionJson = Definition(), CreatedAtUtc = DateTime.UtcNow, EnableBlackboardSnapshots = true,
        Nodes = BootstrapSteps.Select(x => new AgentRunNode { Id = Guid.NewGuid(), NodeKey = x.Key, NodeType = x.Type, Status = AgentNodeStatuses.Pending }).ToList()
    };

    public string CreateInitialBlackboardJson(Guid researchRunId) => Initial(researchRunId, new ResearchAskRequest(string.Empty, string.Empty));

    public string CreateInitialBlackboardJson(Guid researchRunId, ResearchAskRequest request) => Initial(researchRunId, request);

    private static string Initial(Guid researchRunId, ResearchAskRequest request) => new JsonObject
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
        var plan = planner.BuildPlan(request.Question, request.RetrievalMode, request.DocumentType, topK);
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

public sealed class EvaluateInitialEvidencePolicyNodeHandler : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.EvaluateEvidence;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var local = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence);
        var freshnessSensitive = ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question);
        var useWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb
            || request.SourcePolicy == SourcePolicy.LocalThenWeb && (local.Count == 0 || freshnessSensitive)
            || request.SourcePolicy == SourcePolicy.Auto && freshnessSensitive;
        var output = new { useWeb, localCount = local.Count, freshnessSensitive, sourcePolicy = request.SourcePolicy.ToString() }; board[AgentBlackboardKeys.InitialEvidencePolicy] = JsonSerializer.SerializeToNode(output, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, output); return Task.CompletedTask;
    }
}

public sealed class RetrieveWebResearchEvidenceNodeHandler(IWebRetriever retriever, IOptions<RetrievalOptions> options) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.RetrieveWeb;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var evidence = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence);
        var useWeb = board[AgentBlackboardKeys.InitialEvidencePolicy]?["useWeb"]?.GetValue<bool>() == true;
        if (useWeb) evidence.AddRange(await retriever.RetrieveWebAsync(request.Question, options.Value.WebSearchCandidateCount, options.Value.WebSearchFreshness, cancellationToken));
        board[AgentBlackboardKeys.InitialEvidence] = JsonSerializer.SerializeToNode(evidence, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, new { source = "Web", used = useWeb, totalCount = evidence.Count });
    }
}

public sealed class RankAndSelectResearchEvidenceNodeHandler(IResultReranker reranker, IOptions<RetrievalOptions> options) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.RankEvidence;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var evidence = ResearchInvestigationBoard.Required<List<RetrievedDocumentChunk>>(board, AgentBlackboardKeys.InitialEvidence); var intent = ResearchInvestigationBoard.Required<IntentDetectionResult>(board, AgentBlackboardKeys.ResearchIntent); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest);
        var topK = Math.Clamp(request.TopK <= 0 ? options.Value.DefaultTopK : request.TopK, 1, options.Value.MaxTopK); var ranked = await reranker.Rank(evidence, intent.Selected, topK);
        board[AgentBlackboardKeys.SelectedEvidence] = JsonSerializer.SerializeToNode(ranked, AgentNodeJson.SerializerOptions); ResearchInvestigationBoard.Commit(context, board, new { candidateCount = evidence.Count, selectedCount = ranked.SelectedResults.Count });
    }
}

public sealed class DraftResearchAnswerNodeHandler(IContextSelector selector, IContextFormatter formatter, IAnswerGenerator generator, EquityLensDbContext db) : IAgentNodeHandler
{
    public string NodeType => ResearchInvestigationNodeTypes.DraftAnswer;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchInvestigationBoard.Parse(context.Run); var request = ResearchInvestigationBoard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest); var plan = ResearchInvestigationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan); var intent = ResearchInvestigationBoard.Required<IntentDetectionResult>(board, AgentBlackboardKeys.ResearchIntent); var ranked = ResearchInvestigationBoard.Required<RankedSelection>(board, AgentBlackboardKeys.SelectedEvidence);
        var selected = selector.Select(ranked, intent.Selected, plan); var answer = selected.Chunks.Count == 0 ? new AnswerGenerationResult("目前提供的資料不足以回答此問題。", generator.Model, 0, 0, 0, false, []) : await generator.GenerateAsync(request.Question, formatter.Format(selected.Chunks), selected.RetrievalNote, Math.Clamp(request.Temperature, 0, 1), selected.Chunks.Count, cancellationToken);
        var citations = selected.Chunks.Select(x => new ResearchCitation(x.Index, x.Chunk.SourceType, x.Chunk.SourceType == CitationSourceType.LocalDocument ? x.Chunk.Result.DocumentChunkId : null, x.Chunk.Result.DocumentId, x.Chunk.Result.DocumentTitle, x.Chunk.Result.DocumentType, x.Chunk.SourceRole, x.Chunk.Result.PageNumber, x.Chunk.Url, x.Chunk.PublishedAt, x.Chunk.RetrievedAt, Trim(x.Chunk.Result.Content), x.Chunk.Result.RelevanceScore)).ToList();
        var status = selected.Chunks.Count == 0 ? "InsufficientEvidence" : answer.CitationValidationFailed ? "CitationValidationFailed" : "Answered";
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
