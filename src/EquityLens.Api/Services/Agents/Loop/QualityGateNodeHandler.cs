using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>合併品質檢查；每個已完成階段與結果一併持久化，恢復不重複已提交的評論。</summary>
public sealed class QualityGateNodeHandler(ICriticReviewAgent critic, IEnumerable<IWorkflowPolicyEvaluator> policies) : IAgentNodeHandler
{
    public const string Type = "QualityGate";
    public string NodeType => Type;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stages = new (string Key, IAgentNodeHandler Handler)[]
        {
            ("output", new ResearchOutputGateNodeHandler()),
            ("packet", new BuildEvidencePacketNodeHandler()),
            ("evidence", new CheckEvidenceNodeHandler()),
            ("critic", new CritiqueAnswerNodeHandler(critic)),
            ("report", new FinalizeCriticReportNodeHandler(policies)),
            ("route", new RouteResearchAgentQualityNodeHandler())
        };
        var checkpointKey = $"qualityGate:{context.Node.NodeKey}:{context.Run.ExecutionAttempt}";
        foreach (var stage in stages)
        {
            var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
            if (board[checkpointKey]?[stage.Key] is not null) continue;
            var previousOutput = context.Run.OutputJson;
            await stage.Handler.ExecuteAsync(context, cancellationToken);
            context.Run.OutputJson = previousOutput;
            board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
            var checkpoint = board[checkpointKey] as JsonObject ?? new JsonObject();
            checkpoint[stage.Key] = new JsonObject { ["input"] = JsonNode.Parse(context.Node.InputJson ?? "{}"), ["output"] = JsonNode.Parse(context.Node.OutputJson ?? "{}") };
            if (board[checkpointKey] is null) board[checkpointKey] = checkpoint;
            context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
            await context.DbContext.SaveChangesAsync(cancellationToken);
        }
        var final = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        context.Node.OutputJson = new JsonObject
        {
            ["mode"] = context.Run.WorkflowType == AgentWorkflowTypes.CriticReview ? "Score" : "Research",
            ["outputGate"] = final["outputGate"]?.DeepClone(),
            ["criticReview"] = final[AgentBlackboardKeys.CriticReview]?.DeepClone(),
            ["decision"] = final["qualityRoute"]?["decision"]?.DeepClone()
        }.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}
