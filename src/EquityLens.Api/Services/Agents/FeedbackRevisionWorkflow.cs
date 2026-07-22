using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class FeedbackRevisionWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.FeedbackRevision;

    public AgentRun CreateRun(Guid userId, Guid parentAgentRunId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, ParentAgentRunId = parentAgentRunId,
        WorkflowType = WorkflowType, AgentType = AgentTypes.Research, Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new { parentAgentRunId }),
        BlackboardJson = CreateInitialBlackboardJson(parentAgentRunId),
        WorkflowDefinitionJson = Definition(), CreatedAtUtc = DateTime.UtcNow,
        EnableBlackboardSnapshots = true,
        Nodes =
        [
            new() { Id = Guid.NewGuid(), NodeKey = FeedbackRevisionNodeKeys.LoadContext, NodeType = FeedbackRevisionNodeTypes.LoadContext, Status = AgentNodeStatuses.Pending },
            new() { Id = Guid.NewGuid(), NodeKey = FeedbackRevisionNodeKeys.ValidateContext, NodeType = FeedbackRevisionNodeTypes.ValidateContext, Status = AgentNodeStatuses.Pending }
        ]
    };

    public string CreateInitialBlackboardJson(Guid parentAgentRunId) => new JsonObject
    {
        [AgentBlackboardKeys.ParentAgentRunId] = parentAgentRunId,
        [AgentBlackboardKeys.ParentResearchRunId] = null,
        [AgentBlackboardKeys.ResearchRunId] = null,
        [AgentBlackboardKeys.ResearchRequest] = null,
        [AgentBlackboardKeys.Ticker] = null,
        [AgentBlackboardKeys.Question] = null,
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.OriginalAnswer] = null,
        [AgentBlackboardKeys.FeedbackId] = null,
        [AgentBlackboardKeys.FeedbackComment] = null,
        [AgentBlackboardKeys.FeedbackIntent] = null,
        [AgentBlackboardKeys.RevisionContext] = null,
        [AgentBlackboardKeys.InitialEvidence] = new JsonArray(),
        [AgentBlackboardKeys.SelectedEvidence] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.CriticFindings] = new JsonArray(),
        [AgentBlackboardKeys.CriticReview] = null,
        [AgentBlackboardKeys.RequiredResearchDimensions] = new JsonArray(),
        [AgentBlackboardKeys.MissingResearchDimensions] = new JsonArray(),
        [AgentBlackboardKeys.MathResults] = new JsonArray(),
        [AgentBlackboardKeys.FinalOutput] = null,
        ["schemaVersion"] = 1,
        ["blackboardVersion"] = 0
    }.ToJsonString(AgentNodeJson.SerializerOptions);

    private static string Definition() => new JsonObject
    {
        ["workflowType"] = AgentWorkflowTypes.FeedbackRevision,
        ["version"] = FeedbackRevisionWorkflow.Version,
        ["orchestrationMode"] = "DynamicStateful",
        ["goalStatus"] = "PendingPlanning",
        ["planningHistory"] = new JsonArray(),
        ["nodes"] = new JsonArray
        {
            new JsonObject { ["id"] = FeedbackRevisionNodeKeys.LoadContext, ["type"] = FeedbackRevisionNodeTypes.LoadContext, ["required"] = true },
            new JsonObject { ["id"] = FeedbackRevisionNodeKeys.ValidateContext, ["type"] = FeedbackRevisionNodeTypes.ValidateContext, ["required"] = true }
        },
        ["edges"] = new JsonArray { new JsonObject { ["from"] = FeedbackRevisionNodeKeys.LoadContext, ["to"] = FeedbackRevisionNodeKeys.ValidateContext } }
    }.ToJsonString(AgentNodeJson.SerializerOptions);
}

public sealed class LoadFeedbackRevisionContextNodeHandler : IAgentNodeHandler
{
    public string NodeType => FeedbackRevisionNodeTypes.LoadContext;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var parentId = context.Run.ParentAgentRunId
            ?? throw new AgentNodeException("parent_run_missing", AgentNodeErrorCategories.ValidationFailure, "Feedback revision is missing its parent run.");
        var parent = await context.DbContext.AgentRuns.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == parentId && x.UserId == context.Run.UserId, cancellationToken)
            ?? throw new AgentNodeException("parent_run_not_found", AgentNodeErrorCategories.PermanentFailure, "Parent run not found.");
        if (parent.Status != AgentRunStatuses.Succeeded || parent.ResearchRunId is null
            || parent.WorkflowType is not (AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision))
            throw new AgentNodeException("parent_run_invalid", AgentNodeErrorCategories.ValidationFailure, "Parent run is not a completed research run.");
        var feedback = await context.DbContext.AgentFeedback.AsNoTracking().SingleAsync(
            x => x.FollowUpAgentRunId == context.Run.Id && x.AgentRunId == parent.Id, cancellationToken);
        var childResearchId = context.Run.ResearchRunId
            ?? throw new AgentNodeException("research_run_missing", AgentNodeErrorCategories.ValidationFailure, "Feedback revision is missing its research artifact.");
        var sourceResearch = await context.DbContext.ResearchRuns.AsNoTracking().SingleAsync(x => x.Id == parent.ResearchRunId, cancellationToken);
        var parentBoard = AgentNodeJson.ParseBlackboard(parent.BlackboardJson);
        var request = parentBoard[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions)
            ?? new ResearchAskRequest(sourceResearch.Ticker, sourceResearch.Question,
                Enum.TryParse<RetrievalMode>(sourceResearch.RetrievalMode, true, out var retrievalMode) ? retrievalMode : null,
                sourceResearch.DocumentType,
                Enum.TryParse<SourcePolicy>(sourceResearch.SourcePolicy, true, out var sourcePolicy) ? sourcePolicy : SourcePolicy.Auto,
                sourceResearch.TopK, sourceResearch.Temperature);
        var response = JsonNode.Parse(feedback.ResponseJson ?? "{}") as JsonObject;
        var comment = response?["comment"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var intent = Classify(comment);

        var board = parentBoard.DeepClone().AsObject();
        board[AgentBlackboardKeys.ParentAgentRunId] = parent.Id;
        board[AgentBlackboardKeys.ParentResearchRunId] = sourceResearch.Id;
        board[AgentBlackboardKeys.ResearchRunId] = childResearchId;
        board[AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(request, AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.Ticker] = sourceResearch.Ticker;
        board[AgentBlackboardKeys.Question] = sourceResearch.Question;
        board[AgentBlackboardKeys.Answer] = sourceResearch.Answer;
        board[AgentBlackboardKeys.OriginalAnswer] = sourceResearch.Answer;
        board[AgentBlackboardKeys.FeedbackId] = feedback.Id;
        board[AgentBlackboardKeys.FeedbackComment] = comment;
        board[AgentBlackboardKeys.FeedbackIntent] = intent;
        board[AgentBlackboardKeys.RevisionContext] = new JsonObject
        {
            ["parentAgentRunId"] = parent.Id, ["parentResearchRunId"] = sourceResearch.Id,
            ["feedbackId"] = feedback.Id, ["comment"] = comment, ["intent"] = intent
        };
        board[AgentBlackboardKeys.CriticReview] = null;
        board[AgentBlackboardKeys.CriticFindings] = new JsonArray();
        board[AgentBlackboardKeys.RequiredResearchDimensions] = new JsonArray(comment);
        board[AgentBlackboardKeys.MissingResearchDimensions] = new JsonArray(comment);
        board[AgentBlackboardKeys.RevisedAnswer] = null;
        board[AgentBlackboardKeys.RevisionSummary] = null;
        board[AgentBlackboardKeys.FinalOutput] = null;
        board["dynamicLastNodeKey"] = null;
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = AgentNodeJson.Serialize(new { parentAgentRunId = parent.Id, parentResearchRunId = sourceResearch.Id, feedbackId = feedback.Id, intent });
        context.AddEvent(context.Run, context.Node, AgentEventTypes.FeedbackContextLoaded, "Feedback revision context loaded.", new { feedback.Id, intent, parentAgentRunId = parent.Id });
    }

    private static string Classify(string comment)
    {
        if (Contains(comment, "計算", "重算", "報酬率", "波動率", "var", "drawdown", "夏普")) return "Calculation";
        if (Contains(comment, "結論", "重新分析", "邏輯", "推論", "判斷")) return "Reanalysis";
        if (Contains(comment, "引用", "證據", "來源", "數字", "財報", "最新", "錯誤")) return "EvidenceCorrection";
        return "Wording";
    }
    private static bool Contains(string value, params string[] terms) => terms.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class ValidateFeedbackRevisionContextNodeHandler : IAgentNodeHandler
{
    public string NodeType => FeedbackRevisionNodeTypes.ValidateContext;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var comment = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(comment) || comment.Length is < 5 or > 2000)
            throw new AgentNodeException("feedback_comment_invalid", AgentNodeErrorCategories.ValidationFailure, "Feedback correction comment must contain 5 to 2000 characters.");
        context.Node.OutputJson = AgentNodeJson.Serialize(new { valid = true, intent = board[AgentBlackboardKeys.FeedbackIntent]?.GetValue<string>() });
        return Task.CompletedTask;
    }
}
