using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

internal static class EvidenceRemediationBoard
{
    public static JsonObject Parse(AgentRun run) => AgentNodeJson.ParseBlackboard(run.BlackboardJson);
    public static T Required<T>(JsonObject board, string key) => JsonSerializer.Deserialize<T>(board[key]?.ToJsonString() ?? throw new InvalidOperationException($"Evidence remediation blackboard key '{key}' is missing."), AgentNodeJson.SerializerOptions)!;
    public static void Set<T>(JsonObject board, string key, T value) => board[key] = JsonSerializer.SerializeToNode(value, AgentNodeJson.SerializerOptions);
    public static void Commit(AgentNodeExecutionContext context, JsonObject board, object output) { context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions); context.Node.OutputJson = AgentNodeJson.Serialize(output); }
}

internal static class EvidenceRemediationToolCall
{
    public static async Task<T> RunAsync<T>(AgentNodeExecutionContext context, string toolName, object arguments, Func<Task<T>> action, Func<T, string> preview, CancellationToken cancellationToken)
    {
        var call = new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id, ToolName = toolName, Status = AgentToolCallStatuses.Running, ArgumentsJson = AgentNodeJson.Serialize(arguments), StartedAtUtc = DateTime.UtcNow };
        context.DbContext.AgentToolCalls.Add(call);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallStarted, $"Tool {toolName} started.", null);
        await context.DbContext.SaveChangesAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action(); stopwatch.Stop(); call.Status = AgentToolCallStatuses.Succeeded; call.ResultJson = AgentNodeJson.Serialize(result); call.ResultPreview = AgentNodeJson.Trim(preview(result), 180); call.CompletedAtUtc = DateTime.UtcNow; call.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallCompleted, $"Tool {toolName} completed.", new { call.DurationMs });
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop(); call.Status = AgentToolCallStatuses.Failed; call.ErrorMessage = exception.Message; call.CompletedAtUtc = DateTime.UtcNow; call.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(context.Run, context.Node, AgentEventTypes.ToolCallFailed, $"Tool {toolName} failed.", new { error = exception.Message });
            await context.DbContext.SaveChangesAsync(cancellationToken); throw;
        }
    }
}

public sealed class LoadEvidenceRemediationContextNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.LoadContext;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run);
        var sourceId = board[AgentBlackboardKeys.CriticReviewRunId]?.GetValue<Guid>() ?? throw new InvalidOperationException("Critic review run id is missing.");
        context.Node.InputJson = AgentNodeJson.Serialize(new { criticReviewRunId = sourceId });
        var source = await context.DbContext.AgentRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId && x.UserId == context.Run.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Critic review run not found.");
        if (source.WorkflowType != AgentWorkflowTypes.CriticReview) throw new InvalidOperationException("Source run is not a CriticReview workflow.");
        if (source.Status != AgentRunStatuses.Succeeded) throw new InvalidOperationException("Critic review run has not succeeded.");
        var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson);
        var (review, reviewSource) = ResolveFinalReview(source, sourceBoard);
        var routingValue = review[CriticReviewFields.RequiresMoreEvidence];
        var requiresMoreEvidence = routingValue is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var required) && required;
        if (!requiresMoreEvidence)
        {
            context.AddEvent(context.Run, context.Node, AgentEventTypes.SupervisorDecision, "Critic review routing decision rejected evidence remediation.", new { reviewSource, requiresMoreEvidenceRaw = routingValue?.ToJsonString() });
            throw new InvalidOperationException("Critic review does not require more evidence.");
        }
        board[AgentBlackboardKeys.CriticReviewRun] = JsonSerializer.SerializeToNode(new { source.Id, source.WorkflowType, source.Status }, AgentNodeJson.SerializerOptions);
        foreach (var key in new[] { AgentBlackboardKeys.ResearchRunId, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Candidates }) board[key] = sourceBoard[key]?.DeepClone();
        board[AgentBlackboardKeys.CriticReview] = review.DeepClone();
        board[AgentBlackboardKeys.CriticFindings] = review[CriticReviewFields.Findings]?.DeepClone() ?? new JsonArray();
        var output = new { sourceRunId = source.Id, requiresMoreEvidence = true, findingCount = review[CriticReviewFields.Findings]?.AsArray().Count ?? 0 };
        EvidenceRemediationBoard.Commit(context, board, output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "CriticReview snapshot loaded for evidence remediation.", output);
    }

    private static (JsonObject Review, string Source) ResolveFinalReview(AgentRun source, JsonObject sourceBoard)
    {
        if (!string.IsNullOrWhiteSpace(source.OutputJson))
        {
            try
            {
                return (JsonNode.Parse(source.OutputJson)?.AsObject()
                    ?? throw new InvalidOperationException("Critic review output is invalid."), "OutputJson");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Critic review output is invalid.", exception);
            }
            catch (InvalidOperationException exception) when (exception.Message != "Critic review output is invalid.")
            {
                throw new InvalidOperationException("Critic review output is invalid.", exception);
            }
        }

        if (sourceBoard[AgentBlackboardKeys.FinalOutput] is JsonObject finalOutput) return (finalOutput, "Blackboard.FinalOutput");
        if (sourceBoard[AgentBlackboardKeys.CriticReview] is JsonObject criticReview) return (criticReview, "Blackboard.CriticReview");
        throw new InvalidOperationException("Critic review output is missing.");
    }
}

public sealed class PlanEvidenceRetrievalNodeHandler(IEvidenceRetrievalPlanAgent planner) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.PlanRetrieval;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var question = board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
        var findings = board[AgentBlackboardKeys.CriticFindings]?.AsArray().Select(AgentNodeJson.ParseFinding).Where(x => x is not null).Cast<CriticFinding>().ToList() ?? [];
        var unresolved = board[AgentBlackboardKeys.UnresolvedClaims]?.AsArray().Select(x => x?.GetValue<string>() ?? string.Empty).Where(x => x.Length > 0).ToList() ?? [];
        var previous = board[AgentBlackboardKeys.RetrievalPlan] is null ? [] : EvidenceRemediationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan).Searches.Select(x => x.Query).ToList();
        context.Node.InputJson = AgentNodeJson.Serialize(new { question, findings = findings.Count, unresolvedClaims = unresolved.Count, context.Node.Iteration });
        var result = await EvidenceRemediationToolCall.RunAsync(context, "evidenceRetrievalPlanner", new { context.Node.Iteration, unresolvedClaims = unresolved.Count, previousQueries = previous.Count, promptTemplateId = "evidence-retrieval-planner", promptVersion = 2 }, () => planner.PlanAsync(new(question, unresolved, findings, previous, context.Node.Iteration), cancellationToken), x => $"{x.Mode}: {x.Plan.Searches.Count} searches", cancellationToken);
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RetrievalPlan, result.Plan); EvidenceRemediationBoard.Commit(context, board, result);
    }
}

public sealed class RetrieveRemediationEvidenceNodeHandler(IDocumentRetriever documents, IWebRetriever web) : IAgentNodeHandler
{
    private const int MinimumLocalEvidence = 2;
    public string NodeType => EvidenceRemediationNodeTypes.RetrieveEvidence;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var plan = ResolvePlan(context.Node.InputJson, board); var ticker = board[AgentBlackboardKeys.Ticker]?.GetValue<string>() ?? string.Empty;
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RetrievalPlan, plan);
        var local = await EvidenceRemediationToolCall.RunAsync(context, "documentRetrieval", new { ticker, plan }, () => documents.RetrieveAsync(plan, ticker, cancellationToken), x => $"{x.Count} local candidates", cancellationToken);
        var roundEvidence = local.OrderByDescending(x => x.Result.RelevanceScore).Take(8).ToList(); var usedWebFallback = roundEvidence.Count < MinimumLocalEvidence;
        if (usedWebFallback)
        {
            var query = plan.Searches.FirstOrDefault()?.Query ?? board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
            var freshness = plan.Searches.FirstOrDefault()?.Freshness;
            var webResults = await EvidenceRemediationToolCall.RunAsync(context, "webSearch", new { query, count = 5, freshness }, () => web.RetrieveWebAsync(query, 5, freshness, cancellationToken), x => $"{x.Count} web candidates", cancellationToken);
            roundEvidence.AddRange(webResults.Take(5));
        }
        var existing = context.Node.Iteration > 1 && board[AgentBlackboardKeys.RetrievedEvidence] is not null ? EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence) : [];
        var additions = roundEvidence.Select(x => new RemediationEvidenceItem(0, x.SourceType.ToString(), x.Result.DocumentTitle, x.Result.DocumentType, x.Url ?? x.Result.SourceUrl, x.Result.Content, x.Result.RelevanceScore));
        var evidence = existing.Concat(additions).GroupBy(x => new { x.SourceType, x.Title, x.Url, x.Content }).Select(x => x.OrderByDescending(y => y.RelevanceScore).First()).Take(16).Select((x, i) => x with { Index = i + 1 }).ToList();
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RetrievedEvidence, evidence);
        var history = board[AgentBlackboardKeys.RetrievalHistory]?.AsArray() ?? new JsonArray(); history.Add(JsonSerializer.SerializeToNode(new { iteration = context.Node.Iteration, localCount = local.Count, totalCount = evidence.Count, usedWebFallback }, AgentNodeJson.SerializerOptions)); board[AgentBlackboardKeys.RetrievalHistory] = history;
        var runtime = board[AgentBlackboardKeys.Runtime]?.AsObject() ?? new JsonObject(); if (usedWebFallback) runtime["webFallbackCount"] = (runtime["webFallbackCount"]?.GetValue<int>() ?? 0) + 1; board[AgentBlackboardKeys.Runtime] = runtime;
        var output = new { iteration = context.Node.Iteration, localCount = local.Count, totalCount = evidence.Count, usedWebFallback }; EvidenceRemediationBoard.Commit(context, board, output);
    }

    private static ResearchRetrievalStrategy ResolvePlan(string? inputJson, JsonObject board)
    {
        if (!string.IsNullOrWhiteSpace(inputJson))
        {
            var input = JsonNode.Parse(inputJson) as JsonObject;
            if (input?["searchIntents"] is JsonArray intents && intents.Count > 0)
            {
                var searches = intents.OfType<JsonObject>().Select(intent =>
                {
                    var topic = intent["topic"]?.GetValue<string>() ?? board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
                    var claims = intent["targetClaims"] is JsonArray values ? values.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : [];
                    var query = claims.Count == 0 ? topic : $"{topic} {string.Join(" ", claims)}";
                    var roles = intent["preferredSourceRoles"] as JsonArray; var role = roles?.FirstOrDefault()?.GetValue<string>() ?? "Primary";
                    return new ResearchRetrievalSearch(null, role, query, intent["topK"]?.GetValue<int>() ?? 5, "Compiled from workflow planner search intent.", claims.FirstOrDefault(), intent["freshness"]?.GetValue<string>());
                }).ToList();
                return new("DynamicIntent", searches);
            }
        }
        return EvidenceRemediationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan);
    }
}

public sealed class ExtractAnswerClaimsNodeHandler(IClaimExtractionAgent agent) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.ExtractClaims;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var answer = board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty; context.Node.InputJson = AgentNodeJson.Serialize(new { answerLength = answer.Length });
        var claims = await EvidenceRemediationToolCall.RunAsync(context, "claimExtractionLLM", new { answerLength = answer.Length, promptTemplateId = "evidence-remediation-claim-extraction", promptVersion = 1 }, () => agent.ExtractAsync(answer, cancellationToken), x => $"{x.Count} claims", cancellationToken);
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.ExtractedClaims, claims); EvidenceRemediationBoard.Commit(context, board, claims);
    }
}

public sealed class AssessClaimSupportNodeHandler(IEvidenceAssessor assessor) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.AssessSupport;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var claims = EvidenceRemediationBoard.Required<List<EvidenceClaim>>(board, AgentBlackboardKeys.ExtractedClaims); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence);
        var question = board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty; var sourceAnswer = board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty;
        var findings = board[AgentBlackboardKeys.CriticFindings]?.AsArray().Select(AgentNodeJson.ParseFinding).Where(x => x is not null).Cast<CriticFinding>().ToList() ?? [];
        context.Node.InputJson = AgentNodeJson.Serialize(new { question, answerLength = sourceAnswer.Length, claimCount = claims.Count, evidenceCount = evidence.Count, findingCount = findings.Count });
        var result = await EvidenceRemediationToolCall.RunAsync(context, "evidenceAssessorLLM", new { agentIdentity = LlmEvidenceAssessor.AgentIdentity, claimCount = claims.Count, evidenceCount = evidence.Count, promptTemplateId = LlmEvidenceAssessor.PromptTemplateId, promptVersion = LlmEvidenceAssessor.PromptVersion }, () => assessor.AssessAsync(new(question, sourceAnswer, claims, evidence, findings), cancellationToken), x => $"{x.Assessments.Count} assessments; {x.Provider}/{x.Model}; {x.PromptTokens + x.CompletionTokens} tokens; cost={x.EstimatedCostUsd?.ToString() ?? "unavailable"}", cancellationToken);
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.ClaimSupportAssessments, result.Assessments); EvidenceRemediationBoard.Commit(context, board, result);
    }
}

public sealed class ValidateEvidenceMappingsNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.ValidateMappings;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var claims = EvidenceRemediationBoard.Required<List<EvidenceClaim>>(board, AgentBlackboardKeys.ExtractedClaims); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); var assessments = EvidenceRemediationBoard.Required<List<ClaimSupportAssessment>>(board, AgentBlackboardKeys.ClaimSupportAssessments);
        var validated = claims.Select(claim => Validate(claim, assessments.FirstOrDefault(x => x.ClaimId == claim.Id), evidence)).ToList(); var supportedCount = validated.Count(x => x.Status == "Supported"); var status = validated.Count > 0 && supportedCount == validated.Count ? "Supported" : supportedCount > 0 ? "PartiallySupported" : "InsufficientEvidence"; var unresolved = validated.Where(x => x.Status != "Supported").Select(x => x.ClaimId).ToList();
        var reanalysisReasons = validated.Select(validatedClaim => (Validated: validatedClaim, Assessment: assessments.FirstOrDefault(x => x.ClaimId == validatedClaim.ClaimId)))
            .Where(x => x.Assessment is not null && x.Validated.ValidationErrors.Count == 0 && x.Validated.EvidenceIndexes.Count > 0 &&
                (x.Validated.Status == "Contradicted" || (x.Validated.Status == "Supported" && x.Assessment.AnalysisImpact == "Material")))
            .Select(x => string.IsNullOrWhiteSpace(x.Assessment!.ImpactReason) ? $"Claim {x.Validated.ClaimId} requires reanalysis ({x.Validated.Status})." : x.Assessment.ImpactReason).Distinct().ToList();
        var result = new EvidenceValidationResult(status, validated, unresolved, reanalysisReasons.Count > 0, reanalysisReasons);
        context.Node.InputJson = AgentNodeJson.Serialize(new { claims = claims.Count, evidence = evidence.Count }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.EvidenceValidationResults, result); EvidenceRemediationBoard.Commit(context, board, result); return Task.CompletedTask;
    }
    private static ValidatedClaimSupport Validate(EvidenceClaim claim, ClaimSupportAssessment? assessment, IReadOnlyList<RemediationEvidenceItem> evidence)
    {
        var errors = new List<string>(); var indexes = assessment?.EvidenceIndexes.Distinct().ToList() ?? [];
        if (assessment is null) errors.Add("Missing support assessment."); if (indexes.Count == 0) errors.Add("Evidence mapping is empty."); if (indexes.Any(i => i < 1 || i > evidence.Count)) errors.Add("Evidence index is out of range.");
        if (assessment is not null && (assessment.Confidence < 0 || assessment.Confidence > 1)) errors.Add("Confidence must be between 0 and 1.");
        if (assessment is not null && assessment.AnalysisImpact is not ("None" or "WordingOnly" or "Material")) errors.Add("Analysis impact is invalid.");
        var selectedText = string.Join(" ", indexes.Where(i => i >= 1 && i <= evidence.Count).Select(i => evidence[i - 1].Content)); foreach (var number in claim.NumericValues) if (!selectedText.Contains(number, StringComparison.OrdinalIgnoreCase)) errors.Add($"Numeric value '{number}' is not present in mapped evidence.");
        var status = errors.Count > 0 ? "Unsupported" : assessment?.Status switch { "Supported" or "PartiallySupported" => "Supported", "Contradicted" => "Contradicted", _ => "Unsupported" }; return new ValidatedClaimSupport(claim.Id, claim.Text, status, indexes, errors);
    }
}

public sealed class RouteEvidenceRemediationNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.Route;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run);
        var validation = EvidenceRemediationBoard.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults);
        var decision = validation.EvidenceStatus == "Supported" ? "Supported" : "InsufficientEvidence";
        board[AgentBlackboardKeys.RouteDecision] = decision;
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.UnresolvedClaims, validation.UnresolvedClaimIds);
        board[AgentBlackboardKeys.RequiresReanalysis] = validation.RequiresReanalysis;
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.ReanalysisReasons, validation.ReanalysisReasons ?? []);
        var runtime = board[AgentBlackboardKeys.Runtime]?.AsObject() ?? new JsonObject(); runtime["iteration"] = context.Node.Iteration; board[AgentBlackboardKeys.Runtime] = runtime;
        context.Node.InputJson = AgentNodeJson.Serialize(new { context.Node.Iteration, validation.EvidenceStatus, unresolvedCount = validation.UnresolvedClaimIds.Count });
        EvidenceRemediationBoard.Commit(context, board, new { decision, context.Node.Iteration, unresolvedClaimIds = validation.UnresolvedClaimIds, validation.RequiresReanalysis, reanalysisReasons = validation.ReanalysisReasons ?? [] });
        context.AddEvent(context.Run, context.Node, AgentEventTypes.SupervisorDecision, $"Evidence remediation route: {decision}; reanalysis {(validation.RequiresReanalysis ? "recommended" : "not required")}.", new { decision, context.Node.Iteration, validation.RequiresReanalysis, reanalysisReasons = validation.ReanalysisReasons ?? [] });
        return Task.CompletedTask;
    }
}

public sealed class BuildRemediatedEvidencePacketNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.BuildPacket;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var validation = EvidenceRemediationBoard.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); var packet = new RemediatedEvidencePacket(validation.EvidenceStatus, validation.Claims, evidence, validation.UnresolvedClaimIds, validation.RequiresReanalysis, validation.ReanalysisReasons ?? []); context.Node.InputJson = AgentNodeJson.Serialize(new { validation.EvidenceStatus, evidenceCount = evidence.Count, validation.RequiresReanalysis }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RemediatedEvidencePacket, packet); EvidenceRemediationBoard.Commit(context, board, packet); return Task.CompletedTask;
    }
}

public sealed class DraftEvidenceBackedRevisionNodeHandler(IEvidenceBackedRevisionAgent agent) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.DraftRevision;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var packet = EvidenceRemediationBoard.Required<RemediatedEvidencePacket>(board, AgentBlackboardKeys.RemediatedEvidencePacket); var source = board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty; var question = board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty; context.Node.InputJson = AgentNodeJson.Serialize(new { packet.EvidenceStatus, evidenceCount = packet.Evidence.Count });
        EvidenceBackedRevisionResult result;
        if (packet.EvidenceStatus == "InsufficientEvidence") result = new(source, "補充檢索後仍無足夠證據，保留原回答並標示未解決 claims。");
        else result = await EvidenceRemediationToolCall.RunAsync(context, "evidenceRevisionLLM", new { evidenceCount = packet.Evidence.Count, promptTemplateId = "evidence-remediation-revision", promptVersion = 1 }, () => agent.ReviseAsync(question, source, packet, cancellationToken), x => x.RevisionSummary, cancellationToken);
        board[AgentBlackboardKeys.RevisedAnswer] = result.RevisedAnswer; board[AgentBlackboardKeys.RevisionSummary] = result.RevisionSummary; EvidenceRemediationBoard.Commit(context, board, result);
    }
}

public sealed class FinalizeEvidenceRemediationNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.Finalize;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var validation = EvidenceRemediationBoard.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults); var packet = EvidenceRemediationBoard.Required<RemediatedEvidencePacket>(board, AgentBlackboardKeys.RemediatedEvidencePacket); var output = new EvidenceRemediationOutput(board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisedAnswer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisionSummary]?.GetValue<string>() ?? string.Empty, validation.EvidenceStatus, packet.Evidence, validation.UnresolvedClaimIds, validation.RequiresReanalysis, validation.ReanalysisReasons ?? []); context.Node.InputJson = AgentNodeJson.Serialize(new { validation.EvidenceStatus, unresolvedCount = validation.UnresolvedClaimIds.Count, validation.RequiresReanalysis }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.FinalOutput, output); EvidenceRemediationBoard.Commit(context, board, output); context.Run.OutputJson = context.Node.OutputJson; return Task.CompletedTask;
    }
}
