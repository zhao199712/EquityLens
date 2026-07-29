using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed class WaitForHumanApprovalNodeHandler : IAgentNodeHandler
{
    public string NodeType => HumanApprovalNodeTypes.WaitForHumanApproval;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);

        if (blackboard[AgentBlackboardKeys.ApprovalDecision] is JsonObject decision)
        {
            CompleteWithDecision(run, node, blackboard, decision, context);
            return Task.CompletedTask;
        }

        var (approvalType, prompt, subjectKey) = ReadConfig(run.WorkflowDefinitionJson, node.NodeKey);
        var subject = subjectKey is null ? null : blackboard[subjectKey]?.DeepClone();
        blackboard[AgentBlackboardKeys.ApprovalRequest] = JsonSerializer.SerializeToNode(new
        {
            approvalType,
            prompt,
            nodeKey = node.NodeKey,
            requestedAtUtc = DateTime.UtcNow,
            subject
        }, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.InputJson = AgentNodeJson.Serialize(new { approvalType, prompt, nodeKey = node.NodeKey, subjectKey });
        context.AddEvent(run, node, AgentEventTypes.ApprovalRequested, "Human approval requested.", new { approvalType, prompt });
        context.RequestApproval();
        return Task.CompletedTask;
    }

    private static void CompleteWithDecision(
        AgentRun run,
        AgentRunNode node,
        JsonObject blackboard,
        JsonObject decision,
        AgentNodeExecutionContext context)
    {
        var resolvedDecision = decision[HumanApprovalFields.Decision]?.GetValue<string>();
        var comment = decision[HumanApprovalFields.Comment]?.GetValue<string>();
        var reviewerId = decision[HumanApprovalFields.ReviewerId]?.GetValue<string>();
        var decidedAtUtc = decision[HumanApprovalFields.DecidedAtUtc]?.GetValue<string>();
        blackboard[AgentBlackboardKeys.HumanApproval] = JsonSerializer.SerializeToNode(new
        {
            decision = resolvedDecision,
            comment,
            reviewerId,
            decidedAtUtc
        }, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(new
        {
            decision = resolvedDecision,
            comment,
            reviewerId,
            decidedAtUtc
        });
        context.AddEvent(run, node, AgentEventTypes.ApprovalDecision, "Human approval decision applied.", new { decision = resolvedDecision, reviewerId });
    }

    private static (string ApprovalType, string? Prompt, string? SubjectKey) ReadConfig(string definitionJson, string nodeKey)
    {
        var node = JsonNode.Parse(definitionJson)?["nodes"]?.AsArray().OfType<JsonObject>()
            .SingleOrDefault(x => x["id"]?.GetValue<string>() == nodeKey);
        var config = node?["config"] as JsonObject;
        return (
            config?["approvalType"]?.GetValue<string>() ?? "ApproveReject",
            config?["prompt"]?.GetValue<string>(),
            config?["subjectKey"]?.GetValue<string>());
    }
}
