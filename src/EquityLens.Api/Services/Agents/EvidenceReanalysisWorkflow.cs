using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record ReanalysisClaim(string ClaimId, string ClaimText, string Status, IReadOnlyList<int> EvidenceIndexes);
public sealed record InvestmentReanalysisContext(string? Ticker, string Question, string SourceAnswer, string RemediationAnswer, IReadOnlyList<ReanalysisClaim> AffectedClaims, IReadOnlyList<RemediationEvidenceItem> Evidence, IReadOnlyList<string> ReanalysisReasons);
public sealed record InvestmentReanalysisDraft(string ReanalyzedAnswer, string AnalysisChangeSummary, IReadOnlyList<string> ChangedClaimIds, IReadOnlyList<string> KeyConclusionChanges);
public sealed record InvestmentReanalysisAgentResult(InvestmentReanalysisDraft Draft, string AgentIdentity, string Provider, string Model, int PromptTokens, int CompletionTokens, decimal? EstimatedCostUsd);
public sealed record EvidenceReanalysisOutput(string SourceAnswer, string RemediationAnswer, string ReanalyzedAnswer, string FinalAnswer, string AnalysisChangeSummary, IReadOnlyList<string> ChangedClaimIds, IReadOnlyList<string> KeyConclusionChanges, IReadOnlyList<RemediationEvidenceItem> Citations, CriticReviewResult CriticReview, bool RequiresMoreEvidence, string RecommendedNextAction, IReadOnlyList<string> ReanalysisReasons);

public interface IInvestmentReanalysisAgent
{
    Task<InvestmentReanalysisAgentResult> ReanalyzeAsync(InvestmentReanalysisContext context, CancellationToken cancellationToken = default);
}

public sealed class LlmInvestmentReanalysisAgent(IChatCompletionService chat) : IInvestmentReanalysisAgent
{
    public const string AgentIdentity = "InvestmentReanalysisAgent";
    public const string PromptTemplateId = "evidence-reanalysis-analysis";
    public const int PromptVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InvestmentReanalysisAgentResult> ReanalyzeAsync(InvestmentReanalysisContext context, CancellationToken cancellationToken = default)
    {
        var response = await chat.CompleteAsync(new ChatCompletionRequest(SystemPrompt, JsonSerializer.Serialize(context, JsonOptions), .1, 4096, ChatResponseFormat.JsonObject), cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Content)) throw new InvalidOperationException("Investment reanalysis agent returned empty content.");
        InvestmentReanalysisDraft draft;
        try { draft = JsonSerializer.Deserialize<InvestmentReanalysisDraft>(response.Content, JsonOptions) ?? throw new InvalidOperationException("Investment reanalysis agent returned empty JSON object."); }
        catch (JsonException exception) { throw new InvalidOperationException("Investment reanalysis agent returned invalid JSON content.", exception); }
        Validate(draft, context);
        return new(draft, AgentIdentity, chat.Provider, response.Model, response.PromptTokens, response.CompletionTokens, null);
    }

    private static void Validate(InvestmentReanalysisDraft draft, InvestmentReanalysisContext context)
    {
        if (string.IsNullOrWhiteSpace(draft.ReanalyzedAnswer)) throw new InvalidOperationException("Investment reanalysis result is missing reanalyzedAnswer.");
        if (string.IsNullOrWhiteSpace(draft.AnalysisChangeSummary)) throw new InvalidOperationException("Investment reanalysis result is missing analysisChangeSummary.");
        if (draft.ChangedClaimIds is null || draft.KeyConclusionChanges is null) throw new InvalidOperationException("Investment reanalysis result is missing change details.");
        var allowedClaims = context.AffectedClaims.Select(x => x.ClaimId).ToHashSet(StringComparer.Ordinal);
        if (draft.ChangedClaimIds.Any(x => !allowedClaims.Contains(x))) throw new InvalidOperationException("Investment reanalysis result contains an unknown changed claim id.");
        if (System.Text.RegularExpressions.Regex.Matches(draft.ReanalyzedAnswer, @"\[(\d+)\]").Select(x => int.Parse(x.Groups[1].Value)).Any(x => context.Evidence.All(e => e.Index != x))) throw new InvalidOperationException("Investment reanalysis result contains an invalid citation index.");
    }

    private const string SystemPrompt = """
You are an investment reanalysis agent for Taiwan public equities. Reanalyze only the affected claims using only the supplied validated evidence. Do not search, invent facts or citations, change workflow routing, or introduce an evidence index that was not supplied. Preserve uncertainty. Write Traditional Chinese.
Return JSON only: {"reanalyzedAnswer":"...","analysisChangeSummary":"...","changedClaimIds":["claim-1"],"keyConclusionChanges":["..."]}. Cite supplied evidence as [n].
""";
}

public sealed class EvidenceReanalysisWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    private static readonly (string Key, string Type)[] Steps =
    [
        (EvidenceReanalysisNodeKeys.Load, EvidenceReanalysisNodeTypes.Load), (EvidenceReanalysisNodeKeys.Validate, EvidenceReanalysisNodeTypes.Validate),
        (EvidenceReanalysisNodeKeys.BuildContext, EvidenceReanalysisNodeTypes.BuildContext), (EvidenceReanalysisNodeKeys.Reanalyze, EvidenceReanalysisNodeTypes.Reanalyze),
        (EvidenceReanalysisNodeKeys.Critique, EvidenceReanalysisNodeTypes.Critique), (EvidenceReanalysisNodeKeys.Revise, EvidenceReanalysisNodeTypes.Revise),
        (EvidenceReanalysisNodeKeys.Finalize, EvidenceReanalysisNodeTypes.Finalize)
    ];
    public string WorkflowType => AgentWorkflowTypes.EvidenceReanalysis;
    public AgentRun CreateRun(Guid userId, Guid sourceRunId) => new() { Id = Guid.NewGuid(), UserId = userId, WorkflowType = WorkflowType, AgentType = AgentTypes.Analysis, Status = AgentRunStatuses.Pending, InputJson = AgentNodeJson.Serialize(new { evidenceRemediationRunId = sourceRunId }), BlackboardJson = CreateInitialBlackboardJson(sourceRunId), WorkflowDefinitionJson = Definition().ToJsonString(AgentNodeJson.SerializerOptions), CreatedAtUtc = DateTime.UtcNow, Nodes = Steps.Select(x => new AgentRunNode { Id = Guid.NewGuid(), NodeKey = x.Key, NodeType = x.Type, Status = AgentNodeStatuses.Pending }).ToList() };
    public string CreateInitialBlackboardJson(Guid sourceRunId) => AgentBlackboardContracts.CreateInitialEvidenceReanalysisBlackboard(sourceRunId).ToJsonString(AgentNodeJson.SerializerOptions);
    private static JsonObject Definition() => new() { ["workflowType"] = AgentWorkflowTypes.EvidenceReanalysis, ["version"] = EvidenceReanalysisWorkflow.Version, ["orchestrationMode"] = "Stateful", ["nodes"] = new JsonArray(Steps.Select(x => (JsonNode)new JsonObject { ["id"] = x.Key, ["type"] = x.Type, ["required"] = true }).ToArray()), ["edges"] = new JsonArray(Steps.Zip(Steps.Skip(1), (a, b) => (JsonNode)new JsonObject { ["from"] = a.Key, ["to"] = b.Key }).ToArray()) };
}

internal static class EvidenceReanalysisBoard
{
    public static JsonObject Parse(AgentRun run) => AgentNodeJson.ParseBlackboard(run.BlackboardJson);
    public static T Required<T>(JsonObject board, string key) => JsonSerializer.Deserialize<T>(board[key]?.ToJsonString() ?? throw new InvalidOperationException($"Evidence reanalysis blackboard key '{key}' is missing."), AgentNodeJson.SerializerOptions)!;
    public static void Set<T>(JsonObject board, string key, T value) => board[key] = JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions);
    public static void Commit(AgentNodeExecutionContext context, JsonObject board, object output) { context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(output); }
}

internal sealed record EvidenceReanalysisSourceSnapshot(AgentRun Run, EvidenceRemediationOutput Output, JsonObject Blackboard);

internal static class EvidenceReanalysisSourceValidator
{
    public static async Task<EvidenceReanalysisSourceSnapshot> ValidateAsync(EquityLens.Api.Data.EquityLensDbContext db, Guid userId, Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId && x.UserId == userId, cancellationToken) ?? throw new InvalidOperationException("Evidence remediation run not found.");
        if (source.WorkflowType != AgentWorkflowTypes.EvidenceRemediation) throw new InvalidOperationException("Source run is not an EvidenceRemediation workflow.");
        if (source.Status != AgentRunStatuses.Succeeded) throw new InvalidOperationException("Evidence remediation run has not succeeded.");
        if (string.IsNullOrWhiteSpace(source.OutputJson)) throw new InvalidOperationException("Evidence remediation output is missing.");
        EvidenceRemediationOutput output;
        try { output = JsonSerializer.Deserialize<EvidenceRemediationOutput>(source.OutputJson, AgentNodeJson.SerializerOptions) ?? throw new InvalidOperationException("Evidence remediation output is missing."); }
        catch (JsonException exception) { throw new InvalidOperationException("Evidence remediation output is invalid.", exception); }
        if (!output.RequiresReanalysis) throw new InvalidOperationException("Evidence remediation does not require reanalysis.");
        if (output.ReanalysisReasons is null || output.ReanalysisReasons.Count == 0 || output.ReanalysisReasons.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Reanalysis reasons are missing.");
        var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson);
        RemediatedEvidencePacket packet;
        try { packet = EvidenceReanalysisBoard.Required<RemediatedEvidencePacket>(sourceBoard, AgentBlackboardKeys.RemediatedEvidencePacket); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { throw new InvalidOperationException("Validated evidence packet is missing or invalid.", exception); }
        var evidenceIds = packet.Evidence.Select(x => x.Index).ToHashSet(); var validClaims = packet.Claims.Where(x => x.ValidationErrors.Count == 0 && x.EvidenceIndexes.Count > 0 && x.Status is "Supported" or "Contradicted").ToList();
        if (validClaims.Count == 0) throw new InvalidOperationException("Reanalysis has no validated claims.");
        if (validClaims.SelectMany(x => x.EvidenceIndexes).Any(x => !evidenceIds.Contains(x))) throw new InvalidOperationException("Reanalysis claim mapping contains an invalid evidence index.");
        return new(source, output, sourceBoard);
    }
}

public sealed class LoadEvidenceRemediationNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Load;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var sourceId = board[AgentBlackboardKeys.EvidenceRemediationRunId]?.GetValue<Guid>() ?? throw new InvalidOperationException("Evidence remediation run id is missing.");
        var snapshot = await EvidenceReanalysisSourceValidator.ValidateAsync(context.DbContext, context.Run.UserId, sourceId, cancellationToken); var source = snapshot.Run; var output = snapshot.Output; var sourceBoard = snapshot.Blackboard;
        board[AgentBlackboardKeys.EvidenceRemediationRun] = JsonSerializer.SerializeToNode(new { source.Id, source.WorkflowType, source.Status }, AgentNodeJson.SerializerOptions); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.EvidenceRemediationOutput, output);
        foreach (var key in new[] { AgentBlackboardKeys.ResearchRunId, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.CriticFindings, AgentBlackboardKeys.EvidenceValidationResults, AgentBlackboardKeys.RemediatedEvidencePacket }) board[key] = sourceBoard[key]?.DeepClone();
        board[AgentBlackboardKeys.RequiresReanalysis] = true; EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.ReanalysisReasons, output.ReanalysisReasons ?? []);
        var result = new { sourceRunId = source.Id, output.RequiresReanalysis, reasonCount = output.ReanalysisReasons?.Count ?? 0 }; context.Node.InputJson = AgentNodeJson.Serialize(new { evidenceRemediationRunId = sourceId }); EvidenceReanalysisBoard.Commit(context, board, result);
    }
}

public sealed class ValidateReanalysisRequestNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Validate;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var reasons = EvidenceReanalysisBoard.Required<List<string>>(board, AgentBlackboardKeys.ReanalysisReasons); var packet = EvidenceReanalysisBoard.Required<RemediatedEvidencePacket>(board, AgentBlackboardKeys.RemediatedEvidencePacket);
        if (board[AgentBlackboardKeys.RequiresReanalysis]?.GetValue<bool>() != true) throw new InvalidOperationException("Reanalysis request is not required.");
        if (reasons.Count == 0 || reasons.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Reanalysis reasons are missing.");
        var evidenceIds = packet.Evidence.Select(x => x.Index).ToHashSet(); var validClaims = packet.Claims.Where(x => x.ValidationErrors.Count == 0 && x.EvidenceIndexes.Count > 0 && x.Status is "Supported" or "Contradicted").ToList();
        if (validClaims.Count == 0) throw new InvalidOperationException("Reanalysis has no validated claims.");
        if (validClaims.SelectMany(x => x.EvidenceIndexes).Any(x => !evidenceIds.Contains(x))) throw new InvalidOperationException("Reanalysis claim mapping contains an invalid evidence index.");
        var result = new { validClaimCount = validClaims.Count, evidenceCount = packet.Evidence.Count, reasonCount = reasons.Count }; context.Node.InputJson = AgentNodeJson.Serialize(new { packet.EvidenceStatus }); EvidenceReanalysisBoard.Commit(context, board, result); return Task.CompletedTask;
    }
}

public sealed class BuildAnalysisContextNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.BuildContext;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var packet = EvidenceReanalysisBoard.Required<RemediatedEvidencePacket>(board, AgentBlackboardKeys.RemediatedEvidencePacket); var reasons = EvidenceReanalysisBoard.Required<List<string>>(board, AgentBlackboardKeys.ReanalysisReasons);
        var output = board[AgentBlackboardKeys.EvidenceRemediationOutput] is not null
            ? EvidenceReanalysisBoard.Required<EvidenceRemediationOutput>(board, AgentBlackboardKeys.EvidenceRemediationOutput)
            : new EvidenceRemediationOutput(board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisedAnswer]?.GetValue<string>() ?? board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisionSummary]?.GetValue<string>() ?? "Dynamic evidence remediation.", packet.EvidenceStatus, packet.Evidence, packet.UnresolvedClaimIds, packet.RequiresReanalysis, packet.ReanalysisReasons);
        var validClaims = packet.Claims.Where(x => x.ValidationErrors.Count == 0 && x.EvidenceIndexes.Count > 0 && x.Status is "Supported" or "Contradicted").ToList(); var used = validClaims.SelectMany(x => x.EvidenceIndexes).ToHashSet(); var selectedEvidence = packet.Evidence.Where(x => used.Contains(x.Index)).OrderBy(x => x.Index).ToList(); var indexMap = selectedEvidence.Select((item, index) => (item.Index, NewIndex: index + 1)).ToDictionary(x => x.Index, x => x.NewIndex); var evidence = selectedEvidence.Select(x => x with { Index = indexMap[x.Index] }).ToList(); var claims = validClaims.Select(x => new ReanalysisClaim(x.ClaimId, x.ClaimText, x.Status, x.EvidenceIndexes.Select(i => indexMap[i]).ToList())).ToList();
        var analysisContext = new InvestmentReanalysisContext(board[AgentBlackboardKeys.Ticker]?.GetValue<string>(), board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty, output.SourceAnswer, output.RevisedAnswer, claims, evidence, reasons); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.AnalysisContext, analysisContext); context.Node.InputJson = AgentNodeJson.Serialize(new { claimCount = claims.Count, evidenceCount = evidence.Count }); EvidenceReanalysisBoard.Commit(context, board, analysisContext); return Task.CompletedTask;
    }
}

public sealed class ReanalyzeAnswerNodeHandler(IInvestmentReanalysisAgent agent) : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Reanalyze;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var input = EvidenceReanalysisBoard.Required<InvestmentReanalysisContext>(board, AgentBlackboardKeys.AnalysisContext); context.Node.InputJson = AgentNodeJson.Serialize(new { claims = input.AffectedClaims.Count, evidence = input.Evidence.Count });
        var result = await EvidenceRemediationToolCall.RunAsync(context, "investmentReanalysisLLM", new { agentIdentity = LlmInvestmentReanalysisAgent.AgentIdentity, promptTemplateId = LlmInvestmentReanalysisAgent.PromptTemplateId, promptVersion = LlmInvestmentReanalysisAgent.PromptVersion, claims = input.AffectedClaims.Count, evidence = input.Evidence.Count }, () => agent.ReanalyzeAsync(input, cancellationToken), x => $"{x.Provider}/{x.Model}; {x.PromptTokens + x.CompletionTokens} tokens", cancellationToken); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.ReanalysisDraft, result.Draft); EvidenceReanalysisBoard.Commit(context, board, result);
    }
}

public sealed class CritiqueReanalysisNodeHandler(ICriticReviewAgent critic, IEnumerable<IWorkflowPolicyEvaluator> policies) : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Critique;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var draft = EvidenceReanalysisBoard.Required<InvestmentReanalysisDraft>(board, AgentBlackboardKeys.ReanalysisDraft); var analysis = EvidenceReanalysisBoard.Required<InvestmentReanalysisContext>(board, AgentBlackboardKeys.AnalysisContext); context.Node.InputJson = AgentNodeJson.Serialize(new { citationCount = analysis.Evidence.Count, answerLength = draft.ReanalyzedAnswer.Length });
        var criticEvidence = analysis.Evidence.Select(x => new CriticEvidenceItem(x.Index, x.Title, x.SourceType, x.Content)).ToList(); var review = await EvidenceRemediationToolCall.RunAsync(context, "reanalysisCriticLLM", new { promptTemplateId = "critic-review", promptVersion = 2, citationCount = analysis.Evidence.Count }, () => critic.CritiqueAsync(new(analysis.Ticker, analysis.Question, draft.ReanalyzedAnswer, analysis.Evidence.Count, analysis.Evidence.Count, "ValidatedEvidence", [], criticEvidence), cancellationToken), x => x.Summary, cancellationToken); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.ReanalysisCriticReview, review);
        var reviewNode = JsonSerializer.SerializeToNode(review, AgentNodeJson.SerializerOptions)!.AsObject(); var policy = policies.Single(x => x.WorkflowType == AgentWorkflowTypes.EvidenceReanalysis); var decision = policy.Evaluate(new(AgentWorkflowTypes.EvidenceReanalysis, context.Node.NodeKey, board, reviewNode)); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.ReanalysisPolicyDecision, decision); EvidenceReanalysisBoard.Commit(context, board, new { review, decision }); context.AddEvent(context.Run, context.Node, AgentEventTypes.SupervisorDecision, $"Reanalysis critic decision: {decision.RecommendedNextAction}.", decision);
    }
}

public sealed class ReviseReanalysisNodeHandler(IDraftRevisionAgent revision) : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Revise;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var analysis = EvidenceReanalysisBoard.Required<InvestmentReanalysisContext>(board, AgentBlackboardKeys.AnalysisContext); var draft = EvidenceReanalysisBoard.Required<InvestmentReanalysisDraft>(board, AgentBlackboardKeys.ReanalysisDraft); var review = EvidenceReanalysisBoard.Required<CriticReviewResult>(board, AgentBlackboardKeys.ReanalysisCriticReview); var decision = EvidenceReanalysisBoard.Required<WorkflowPolicyDecision>(board, AgentBlackboardKeys.ReanalysisPolicyDecision);
        var result = await EvidenceRemediationToolCall.RunAsync(context, "reanalysisRevisionAgent", new { requiresRevision = decision.RequiresRevision, promptTemplateId = "draft-revision", promptVersion = 1 }, () => revision.ReviseAsync(new(analysis.Ticker, analysis.Question, draft.ReanalyzedAnswer, decision.RequiresRevision, review.Summary, review.OverallSeverity, review.Findings, review.SuggestedAnswerRevision, decision.RecommendedNextAction), cancellationToken), x => x.RevisionSummary, cancellationToken); context.Node.InputJson = AgentNodeJson.Serialize(new { decision.RequiresRevision }); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.ReanalysisFinalRevision, result); EvidenceReanalysisBoard.Commit(context, board, result);
    }
}

public sealed class FinalizeReanalysisNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceReanalysisNodeTypes.Finalize;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceReanalysisBoard.Parse(context.Run); var analysis = EvidenceReanalysisBoard.Required<InvestmentReanalysisContext>(board, AgentBlackboardKeys.AnalysisContext); var draft = EvidenceReanalysisBoard.Required<InvestmentReanalysisDraft>(board, AgentBlackboardKeys.ReanalysisDraft); var review = EvidenceReanalysisBoard.Required<CriticReviewResult>(board, AgentBlackboardKeys.ReanalysisCriticReview); var decision = EvidenceReanalysisBoard.Required<WorkflowPolicyDecision>(board, AgentBlackboardKeys.ReanalysisPolicyDecision); var revision = EvidenceReanalysisBoard.Required<DraftRevisionResult>(board, AgentBlackboardKeys.ReanalysisFinalRevision);
        var output = new EvidenceReanalysisOutput(analysis.SourceAnswer, analysis.RemediationAnswer, draft.ReanalyzedAnswer, revision.RevisedAnswer, draft.AnalysisChangeSummary, draft.ChangedClaimIds, draft.KeyConclusionChanges, analysis.Evidence, review, decision.RequiresMoreEvidence, decision.RequiresMoreEvidence ? "EvidenceRemediation" : decision.RecommendedNextAction, analysis.ReanalysisReasons); context.Node.InputJson = AgentNodeJson.Serialize(new { decision.RequiresMoreEvidence, citationCount = analysis.Evidence.Count }); EvidenceReanalysisBoard.Set(board, AgentBlackboardKeys.FinalOutput, output); EvidenceReanalysisBoard.Commit(context, board, output); context.Run.OutputJson = context.Node.OutputJson; return Task.CompletedTask;
    }
}
