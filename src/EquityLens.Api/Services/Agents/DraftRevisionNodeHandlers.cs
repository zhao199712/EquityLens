using System.Text.Json;
using System.Text.Json.Nodes;
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
    public string NodeType => DraftRevisionNodeTypes.DraftRevisedAnswer;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var criticReview = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.CriticReview);
        var sourceAnswer = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Answer);
        var requiresRevision = criticReview[CriticReviewFields.RequiresRevision]?.GetValue<bool>() ?? false;
        var suggestedAnswerRevision = criticReview[CriticReviewFields.SuggestedAnswerRevision]?.GetValue<string>();
        node.InputJson = AgentNodeJson.Serialize(new DraftRevisedAnswerNodeInput(
            AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Ticker),
            AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Question),
            sourceAnswer,
            requiresRevision,
            suggestedAnswerRevision));

        var revisedAnswer = CreateRevisedAnswer(sourceAnswer, requiresRevision, suggestedAnswerRevision);
        var revisionSummary = requiresRevision
            ? "已根據 CriticReview 建議產生 deterministic 修訂稿。"
            : "CriticReview 未要求修訂，保留原回答。";
        var output = new DraftRevisedAnswerNodeOutput(
            sourceAnswer,
            revisedAnswer,
            revisionSummary,
            requiresRevision,
            requiresRevision ? suggestedAnswerRevision : null);
        blackboard[AgentBlackboardKeys.RevisedAnswer] = revisedAnswer;
        blackboard[AgentBlackboardKeys.RevisionSummary] = revisionSummary;
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(output);
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Draft revision written to blackboard.", new { requiresRevision });
        return Task.CompletedTask;
    }

    private static string CreateRevisedAnswer(string? sourceAnswer, bool requiresRevision, string? suggestedAnswerRevision)
    {
        if (!requiresRevision)
        {
            return sourceAnswer ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(suggestedAnswerRevision))
        {
            return suggestedAnswerRevision;
        }

        return string.IsNullOrWhiteSpace(sourceAnswer)
            ? "修訂稿待補：原回答為空，需先補充可引用證據。"
            : $"{sourceAnswer}\n\n修訂提醒：請補強引用支撐，並避免超出來源證據的推論。";
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
        var appliedRecommendation = revisionRequired
            ? criticReview[CriticReviewFields.SuggestedAnswerRevision]?.GetValue<string>()
            : null;
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
