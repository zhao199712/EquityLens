using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class LoadCriticReviewRunNodeHandler : IAgentNodeHandler
{
    public string NodeType => DraftRevisionNodeTypes.LoadCriticReviewRun;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var criticReviewRunId = GetCriticReviewRunId(run.InputJson);
        var input = new LoadCriticReviewRunNodeInput(criticReviewRunId);
        node.InputJson = AgentNodeJson.Serialize(input);

        var sourceRun = await context.DbContext.AgentRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == criticReviewRunId && x.UserId == run.UserId, cancellationToken);
        if (sourceRun is null)
        {
            throw new InvalidOperationException("Critic review run not found.");
        }
        if (!string.Equals(sourceRun.WorkflowType, AgentWorkflowTypes.CriticReview, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Source run is not a CriticReview workflow.");
        }
        if (sourceRun.Status != AgentRunStatuses.Succeeded || string.IsNullOrWhiteSpace(sourceRun.OutputJson))
        {
            throw new InvalidOperationException("Critic review run has no completed output.");
        }

        var sourceBlackboard = AgentNodeJson.ParseBlackboard(sourceRun.BlackboardJson);
        var criticReview = AgentNodeJson.ParseBlackboard(sourceRun.OutputJson);
        var requiresRevision = criticReview[CriticReviewFields.RequiresRevision]?.GetValue<bool>() ?? false;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        blackboard[AgentBlackboardKeys.CriticReviewRun] = JsonSerializer.SerializeToNode(new
        {
            sourceRun.Id,
            sourceRun.WorkflowType,
            sourceRun.Status,
            sourceRun.OutputJson
        }, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.Ticker] = sourceBlackboard[AgentBlackboardKeys.Ticker]?.DeepClone();
        blackboard[AgentBlackboardKeys.Question] = sourceBlackboard[AgentBlackboardKeys.Question]?.DeepClone();
        blackboard[AgentBlackboardKeys.Answer] = sourceBlackboard[AgentBlackboardKeys.Answer]?.DeepClone();
        blackboard[AgentBlackboardKeys.CriticReview] = JsonNode.Parse(sourceRun.OutputJson);
        blackboard[AgentBlackboardKeys.CriticFindings] = criticReview[CriticReviewFields.Findings]?.DeepClone() ?? new JsonArray();
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(new LoadCriticReviewRunNodeOutput(
            sourceRun.Id,
            sourceRun.WorkflowType,
            sourceRun.Status,
            true,
            requiresRevision));
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "CriticReview source run loaded into blackboard.", new { criticReviewRunId, requiresRevision });
    }

    private static Guid GetCriticReviewRunId(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        return document.RootElement.GetProperty("criticReviewRunId").GetGuid();
    }
}

public sealed class DraftRevisedAnswerNodeHandler : IAgentNodeHandler
{
    private readonly IDraftRevisionAgent _draftRevisionAgent;

    public DraftRevisedAnswerNodeHandler(IDraftRevisionAgent draftRevisionAgent)
    {
        _draftRevisionAgent = draftRevisionAgent;
    }

    public string NodeType => DraftRevisionNodeTypes.DraftRevisedAnswer;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var criticReview = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.CriticReview);
        var sourceAnswer = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Answer);
        var requiresRevision = criticReview[CriticReviewFields.RequiresRevision]?.GetValue<bool>() ?? false;
        var suggestedAnswerRevision = criticReview[CriticReviewFields.SuggestedAnswerRevision]?.GetValue<string>();
        var findings = (criticReview[CriticReviewFields.Findings]?.AsArray() ?? [])
            .Select(AgentNodeJson.ParseFinding)
            .Where(x => x is not null)
            .Cast<CriticFinding>()
            .ToList();
        var input = new DraftRevisionInput(
            AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Ticker),
            AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Question),
            sourceAnswer,
            requiresRevision,
            criticReview[CriticReviewFields.Summary]?.GetValue<string>() ?? string.Empty,
            criticReview[CriticReviewFields.OverallSeverity]?.GetValue<string>() ?? "None",
            findings,
            suggestedAnswerRevision,
            CreateAppliedRecommendation(requiresRevision, suggestedAnswerRevision));
        node.InputJson = AgentNodeJson.Serialize(new DraftRevisedAnswerNodeInput(
            input.Ticker,
            input.Question,
            input.SourceAnswer,
            input.RequiresRevision,
            input.SuggestedAnswerRevision));

        var result = requiresRevision
            ? await ReviseWithToolCallAsync(context, input, cancellationToken)
            : await _draftRevisionAgent.ReviseAsync(input, cancellationToken);
        var output = new DraftRevisedAnswerNodeOutput(
            sourceAnswer,
            result.RevisedAnswer,
            result.RevisionSummary,
            requiresRevision,
            result.AppliedRecommendation);
        blackboard[AgentBlackboardKeys.RevisedAnswer] = result.RevisedAnswer;
        blackboard[AgentBlackboardKeys.RevisionSummary] = result.RevisionSummary;
        blackboard[AgentBlackboardKeys.AppliedRecommendation] = result.AppliedRecommendation;
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Draft revision written to blackboard.", new { requiresRevision });
    }

    private static string? CreateAppliedRecommendation(bool requiresRevision, string? suggestedAnswerRevision)
    {
        if (!requiresRevision) return null;
        return string.IsNullOrWhiteSpace(suggestedAnswerRevision)
            ? DeterministicDraftRevisionAgent.FallbackRecommendation
            : suggestedAnswerRevision;
    }

    private async Task<DraftRevisionResult> ReviseWithToolCallAsync(
        AgentNodeExecutionContext context,
        DraftRevisionInput input,
        CancellationToken cancellationToken)
    {
        var run = context.Run;
        var node = context.Node;
        var toolCall = new AgentToolCall
        {
            Id = Guid.NewGuid(),
            AgentRunId = run.Id,
            AgentRunNodeId = node.Id,
            ToolName = "draftRevisionLLM",
            Status = AgentToolCallStatuses.Running,
            ArgumentsJson = AgentNodeJson.Serialize(new
            {
                input.Ticker,
                input.OverallSeverity,
                FindingCount = input.Findings.Count,
                SourceAnswerPreview = AgentNodeJson.Trim(input.SourceAnswer ?? string.Empty, 240),
                input.AppliedRecommendation
            }),
            StartedAtUtc = DateTime.UtcNow
        };
        context.DbContext.AgentToolCalls.Add(toolCall);
        context.AddEvent(run, node, AgentEventTypes.ToolCallStarted, "Tool draftRevisionLLM started.", new { input.Ticker, input.OverallSeverity, FindingCount = input.Findings.Count });
        await context.DbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _draftRevisionAgent.ReviseAsync(input, cancellationToken);
            stopwatch.Stop();
            toolCall.Status = AgentToolCallStatuses.Succeeded;
            toolCall.ResultJson = AgentNodeJson.Serialize(result);
            toolCall.ResultPreview = AgentNodeJson.Trim(result.RevisionSummary, 180);
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(run, node, AgentEventTypes.ToolCallCompleted, "Tool draftRevisionLLM completed.", new { toolCall.DurationMs });
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            toolCall.Status = AgentToolCallStatuses.Failed;
            toolCall.ErrorMessage = exception.Message;
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(run, node, AgentEventTypes.ToolCallFailed, "Tool draftRevisionLLM failed.", new { error = exception.Message });
            await context.DbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}

public sealed class FinalizeRevisionNodeHandler : IAgentNodeHandler
{
    public string NodeType => DraftRevisionNodeTypes.FinalizeRevision;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var criticReview = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.CriticReview);
        var sourceAnswer = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Answer);
        var revisedAnswer = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.RevisedAnswer) ?? string.Empty;
        var revisionSummary = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.RevisionSummary) ?? string.Empty;
        var revisionRequired = criticReview[CriticReviewFields.RequiresRevision]?.GetValue<bool>() ?? false;
        var appliedRecommendation = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.AppliedRecommendation);
        node.InputJson = AgentNodeJson.Serialize(new FinalizeRevisionNodeInput(revisionRequired, revisionSummary));
        var output = new FinalizeRevisionNodeOutput(
            sourceAnswer,
            revisedAnswer,
            revisionSummary,
            revisionRequired,
            appliedRecommendation);
        node.OutputJson = AgentNodeJson.Serialize(output);
        blackboard[AgentBlackboardKeys.FinalOutput] = JsonSerializer.SerializeToNode(output, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        run.OutputJson = node.OutputJson;
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Final draft revision written to blackboard.", new { revisionRequired });
        return Task.CompletedTask;
    }
}
