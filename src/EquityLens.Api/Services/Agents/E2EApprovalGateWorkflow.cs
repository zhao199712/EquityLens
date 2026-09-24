using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

/// <summary>僅供 E2E_TEST_MODE 使用的 deterministic approval workflow。</summary>
public static class E2EApprovalGateWorkflow
{
    public const string WorkflowType = "E2EApprovalGate";
    public const string NodeKey = "executeDeterministicSideEffect";
    public const string NodeType = "E2EExecuteDeterministicSideEffect";
}

public sealed class E2EApprovalGateWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => E2EApprovalGateWorkflow.WorkflowType;

    public AgentRun CreateRun(Guid userId, Guid sourceRunId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WorkflowType = WorkflowType,
        AgentType = AgentTypes.Analysis,
        Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new { sourceRunId }),
        BlackboardJson = CreateInitialBlackboardJson(sourceRunId),
        WorkflowDefinitionJson = new JsonObject
        {
            ["workflowType"] = WorkflowType,
            ["version"] = 1,
            ["nodes"] = new JsonArray(new JsonObject
            {
                ["id"] = E2EApprovalGateWorkflow.NodeKey,
                ["type"] = E2EApprovalGateWorkflow.NodeType,
                ["required"] = true
            }),
            ["edges"] = new JsonArray()
        }.ToJsonString(AgentNodeJson.SerializerOptions),
        CreatedAtUtc = DateTime.UtcNow,
        Nodes =
        [
            new AgentRunNode
            {
                Id = Guid.NewGuid(),
                NodeKey = E2EApprovalGateWorkflow.NodeKey,
                NodeType = E2EApprovalGateWorkflow.NodeType,
                Status = AgentNodeStatuses.Pending
            }
        ]
    };

    public string CreateInitialBlackboardJson(Guid sourceRunId) => new JsonObject
    {
        ["blackboardVersion"] = 0,
        ["sourceRunId"] = sourceRunId,
        ["handlerExecuted"] = false
    }.ToJsonString(AgentNodeJson.SerializerOptions);
}

public sealed class E2EApprovalGateNodeHandler : IAgentNodeHandler
{
    public string NodeType => E2EApprovalGateWorkflow.NodeType;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var output = AgentNodeJson.Serialize(new { handlerExecuted = true });
        context.Node.OutputJson = output;
        context.Run.OutputJson = output;
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        board["handlerExecuted"] = true;
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        return Task.CompletedTask;
    }
}

public sealed class E2EAgentWorkflowCatalog : IAgentWorkflowCatalog
{
    private readonly AgentWorkflowCatalog inner = new();

    public IReadOnlyList<AgentNodeCatalogEntry> Nodes { get; }
    public IReadOnlyList<AgentWorkflowCatalogEntry> Workflows { get; }

    public E2EAgentWorkflowCatalog()
    {
        var node = new AgentNodeCatalogEntry(new AgentNodeContract(
            E2EApprovalGateWorkflow.NodeType, 1, "E2E deterministic side effect",
            "測試 approval 前後 handler 是否執行。", "Act", "ExternalWrite",
            "E2EApprovalInput", "E2EApprovalOutput", [], [], ["handlerExecuted"], [], [],
            new AgentNodeExecutionPolicy(30, 0), true, false, false));
        Nodes = [.. inner.Nodes, node];
        Workflows = [.. inner.Workflows, new AgentWorkflowCatalogEntry(
            E2EApprovalGateWorkflow.WorkflowType, "E2E Approval Gate", "Deterministic approval E2E workflow.",
            AgentTypes.Analysis, [E2EApprovalGateWorkflow.NodeType], [])];
    }

    public AgentWorkflowCatalogEntry GetWorkflow(string workflowType) => Workflows.Single(x => x.WorkflowType == workflowType);
    public AgentNodeCatalogEntry GetNode(string nodeType) => Nodes.Single(x => x.NodeType == nodeType);
}
