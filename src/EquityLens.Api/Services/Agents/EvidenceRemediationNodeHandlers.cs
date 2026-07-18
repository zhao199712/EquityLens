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
        var review = ResolveFinalReview(source, sourceBoard);
        if (!(review[CriticReviewFields.RequiresMoreEvidence]?.GetValue<bool>() ?? false)) throw new InvalidOperationException("Critic review does not require more evidence.");
        board[AgentBlackboardKeys.CriticReviewRun] = JsonSerializer.SerializeToNode(new { source.Id, source.WorkflowType, source.Status }, AgentNodeJson.SerializerOptions);
        foreach (var key in new[] { AgentBlackboardKeys.ResearchRunId, AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.Answer, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Candidates }) board[key] = sourceBoard[key]?.DeepClone();
        board[AgentBlackboardKeys.CriticReview] = review.DeepClone();
        board[AgentBlackboardKeys.CriticFindings] = review[CriticReviewFields.Findings]?.DeepClone() ?? new JsonArray();
        var output = new { sourceRunId = source.Id, requiresMoreEvidence = true, findingCount = review[CriticReviewFields.Findings]?.AsArray().Count ?? 0 };
        EvidenceRemediationBoard.Commit(context, board, output);
        context.AddEvent(context.Run, context.Node, AgentEventTypes.BlackboardUpdated, "CriticReview snapshot loaded for evidence remediation.", output);
    }

    private static JsonObject ResolveFinalReview(AgentRun source, JsonObject sourceBoard)
    {
        if (!string.IsNullOrWhiteSpace(source.OutputJson))
        {
            try
            {
                return JsonNode.Parse(source.OutputJson)?.AsObject()
                    ?? throw new InvalidOperationException("Critic review output is invalid.");
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

        return sourceBoard[AgentBlackboardKeys.FinalOutput] as JsonObject
            ?? sourceBoard[AgentBlackboardKeys.CriticReview] as JsonObject
            ?? throw new InvalidOperationException("Critic review output is missing.");
    }
}

public sealed class PlanEvidenceRetrievalNodeHandler(IRetrievalPlanner planner) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.PlanRetrieval;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var question = board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
        var findings = board[AgentBlackboardKeys.CriticFindings]?.AsArray().Select(AgentNodeJson.ParseFinding).Where(x => x is not null).Cast<CriticFinding>().ToList() ?? [];
        var gapQuery = string.Join("; ", findings.Select(x => $"{x.Category}: {x.Message} {x.Recommendation}"));
        var plan = planner.BuildPlan($"{question}\nEvidence gaps: {gapQuery}", null, null, 5);
        context.Node.InputJson = AgentNodeJson.Serialize(new { question, findings = findings.Count }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RetrievalPlan, plan); EvidenceRemediationBoard.Commit(context, board, plan);
        return Task.CompletedTask;
    }
}

public sealed class RetrieveRemediationEvidenceNodeHandler(IDocumentRetriever documents, IWebRetriever web) : IAgentNodeHandler
{
    private const int MinimumLocalEvidence = 2;
    public string NodeType => EvidenceRemediationNodeTypes.RetrieveEvidence;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var plan = EvidenceRemediationBoard.Required<ResearchRetrievalStrategy>(board, AgentBlackboardKeys.RetrievalPlan); var ticker = board[AgentBlackboardKeys.Ticker]?.GetValue<string>() ?? string.Empty;
        context.Node.InputJson = AgentNodeJson.Serialize(new { ticker, searchCount = plan.Searches.Count, minimumLocalEvidence = MinimumLocalEvidence });
        var local = await EvidenceRemediationToolCall.RunAsync(context, "documentRetrieval", new { ticker, plan }, () => documents.RetrieveAsync(plan, ticker, cancellationToken), x => $"{x.Count} local candidates", cancellationToken);
        var combined = local.OrderByDescending(x => x.Result.RelevanceScore).Take(8).ToList(); var usedWebFallback = combined.Count < MinimumLocalEvidence;
        if (usedWebFallback)
        {
            var query = plan.Searches.FirstOrDefault()?.Query ?? board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
            var webResults = await EvidenceRemediationToolCall.RunAsync(context, "webSearch", new { query, count = 5 }, () => web.RetrieveWebAsync(query, 5, null, cancellationToken), x => $"{x.Count} web candidates", cancellationToken);
            combined.AddRange(webResults.Take(5));
        }
        var evidence = combined.Select((x, i) => new RemediationEvidenceItem(i + 1, x.SourceType.ToString(), x.Result.DocumentTitle, x.Result.DocumentType, x.Url ?? x.Result.SourceUrl, x.Result.Content, x.Result.RelevanceScore)).ToList();
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RetrievedEvidence, evidence); var output = new { localCount = local.Count, totalCount = evidence.Count, usedWebFallback }; EvidenceRemediationBoard.Commit(context, board, output);
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

public sealed class AssessClaimSupportNodeHandler(IClaimSupportAgent agent) : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.AssessSupport;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var claims = EvidenceRemediationBoard.Required<List<EvidenceClaim>>(board, AgentBlackboardKeys.ExtractedClaims); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); context.Node.InputJson = AgentNodeJson.Serialize(new { claimCount = claims.Count, evidenceCount = evidence.Count });
        var assessments = await EvidenceRemediationToolCall.RunAsync(context, "claimSupportLLM", new { claimCount = claims.Count, evidenceCount = evidence.Count, promptTemplateId = "evidence-remediation-support", promptVersion = 1 }, () => agent.AssessAsync(claims, evidence, cancellationToken), x => $"{x.Count} assessments", cancellationToken);
        EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.ClaimSupportAssessments, assessments); EvidenceRemediationBoard.Commit(context, board, assessments);
    }
}

public sealed class ValidateEvidenceMappingsNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.ValidateMappings;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var claims = EvidenceRemediationBoard.Required<List<EvidenceClaim>>(board, AgentBlackboardKeys.ExtractedClaims); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); var assessments = EvidenceRemediationBoard.Required<List<ClaimSupportAssessment>>(board, AgentBlackboardKeys.ClaimSupportAssessments);
        var validated = claims.Select(claim => Validate(claim, assessments.FirstOrDefault(x => x.ClaimId == claim.Id), evidence)).ToList(); var supportedCount = validated.Count(x => x.Status == "Supported"); var status = validated.Count > 0 && supportedCount == validated.Count ? "Supported" : supportedCount > 0 ? "PartiallySupported" : "InsufficientEvidence"; var unresolved = validated.Where(x => x.Status != "Supported").Select(x => x.ClaimId).ToList(); var result = new EvidenceValidationResult(status, validated, unresolved);
        context.Node.InputJson = AgentNodeJson.Serialize(new { claims = claims.Count, evidence = evidence.Count }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.EvidenceValidationResults, result); EvidenceRemediationBoard.Commit(context, board, result); return Task.CompletedTask;
    }
    private static ValidatedClaimSupport Validate(EvidenceClaim claim, ClaimSupportAssessment? assessment, IReadOnlyList<RemediationEvidenceItem> evidence)
    {
        var errors = new List<string>(); var indexes = assessment?.EvidenceIndexes.Distinct().ToList() ?? [];
        if (assessment is null) errors.Add("Missing support assessment."); if (indexes.Any(i => i < 1 || i > evidence.Count)) errors.Add("Evidence index is out of range.");
        var selectedText = string.Join(" ", indexes.Where(i => i >= 1 && i <= evidence.Count).Select(i => evidence[i - 1].Content)); foreach (var number in claim.NumericValues) if (!selectedText.Contains(number, StringComparison.OrdinalIgnoreCase)) errors.Add($"Numeric value '{number}' is not present in mapped evidence.");
        var semanticSupport = assessment?.Status is "Supported" or "PartiallySupported"; var status = semanticSupport && errors.Count == 0 ? "Supported" : "Unsupported"; return new ValidatedClaimSupport(claim.Id, claim.Text, status, indexes, errors);
    }
}

public sealed class BuildRemediatedEvidencePacketNodeHandler : IAgentNodeHandler
{
    public string NodeType => EvidenceRemediationNodeTypes.BuildPacket;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = EvidenceRemediationBoard.Parse(context.Run); var validation = EvidenceRemediationBoard.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults); var evidence = EvidenceRemediationBoard.Required<List<RemediationEvidenceItem>>(board, AgentBlackboardKeys.RetrievedEvidence); var packet = new RemediatedEvidencePacket(validation.EvidenceStatus, validation.Claims, evidence, validation.UnresolvedClaimIds); context.Node.InputJson = AgentNodeJson.Serialize(new { validation.EvidenceStatus, evidenceCount = evidence.Count }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.RemediatedEvidencePacket, packet); EvidenceRemediationBoard.Commit(context, board, packet); return Task.CompletedTask;
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
        var board = EvidenceRemediationBoard.Parse(context.Run); var validation = EvidenceRemediationBoard.Required<EvidenceValidationResult>(board, AgentBlackboardKeys.EvidenceValidationResults); var packet = EvidenceRemediationBoard.Required<RemediatedEvidencePacket>(board, AgentBlackboardKeys.RemediatedEvidencePacket); var output = new EvidenceRemediationOutput(board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisedAnswer]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.RevisionSummary]?.GetValue<string>() ?? string.Empty, validation.EvidenceStatus, packet.Evidence, validation.UnresolvedClaimIds); context.Node.InputJson = AgentNodeJson.Serialize(new { validation.EvidenceStatus, unresolvedCount = validation.UnresolvedClaimIds.Count }); EvidenceRemediationBoard.Set(board, AgentBlackboardKeys.FinalOutput, output); EvidenceRemediationBoard.Commit(context, board, output); context.Run.OutputJson = context.Node.OutputJson; return Task.CompletedTask;
    }
}
