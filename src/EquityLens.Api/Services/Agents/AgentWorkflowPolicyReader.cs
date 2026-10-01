using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

/// <summary>讀取持久化節點政策並補齊新節點的批准政策快照。</summary>
public sealed class AgentWorkflowPolicyReader(
    IAgentWorkflowCatalog? catalog = null,
    IAgentWorkflowAdminService? workflowAdminService = null)
{
    public AgentNodeExecutionPolicy GetExecutionPolicy(string definition, string nodeKey, string nodeType)
    {
        var node = JsonNode.Parse(definition)?["nodes"]?.AsArray().OfType<JsonObject>().SingleOrDefault(x => x["id"]?.GetValue<string>() == nodeKey);
        var policy = node?["executionPolicy"] as JsonObject;
        return policy is null ? catalog?.GetNode(nodeType).DefaultPolicy ?? new AgentNodeExecutionPolicy(120, 0) : new(policy["timeoutSeconds"]!.GetValue<int>(), policy["maxRetryCount"]!.GetValue<int>());
    }

    public AgentApprovalPolicySnapshot GetApprovalPolicy(string definition, string nodeKey, string nodeType)
    {
        var node = JsonNode.Parse(definition)?["nodes"]?.AsArray().OfType<JsonObject>().SingleOrDefault(x => x["id"]?.GetValue<string>() == nodeKey);
        var policy = node?["approvalPolicy"] as JsonObject;
        if (policy is not null)
            return new AgentApprovalPolicySnapshot(
                policy["requiresHumanApproval"]?.GetValue<bool>() ?? false,
                policy["sideEffectLevel"]?.GetValue<string>() ?? "ReadOnly",
                policy["reason"]?.GetValue<string>() ?? $"執行節點「{nodeType}」前需要人工批准。");
        var catalogNode = catalog?.GetNode(nodeType) ?? new AgentWorkflowCatalog().GetNode(nodeType);
        return new AgentApprovalPolicySnapshot(catalogNode.Contract.RequiresHumanInput, catalogNode.SideEffectLevel, $"執行節點「{catalogNode.DisplayName}」前需要人工批准。");
    }

    public async Task SnapshotMissingApprovalPoliciesAsync(AgentRun run, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
        var nodes = root["nodes"]!.AsArray().OfType<JsonObject>().Where(x => x["approvalPolicy"] is null).ToList();
        if (nodes.Count == 0) return;
        var types = nodes.Select(x => x["type"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToArray();
        var policies = workflowAdminService is null
            ? types.ToDictionary(x => x, x => (catalog ?? new AgentWorkflowCatalog()).GetNode(x).Contract.RequiresHumanInput)
            : await workflowAdminService.GetApprovalPoliciesAsync(types, cancellationToken);
        foreach (var node in nodes)
        {
            var type = node["type"]!.GetValue<string>();
            var catalogNode = (catalog ?? new AgentWorkflowCatalog()).GetNode(type);
            node["approvalPolicy"] = new JsonObject
            {
                ["requiresHumanApproval"] = policies[type],
                ["sideEffectLevel"] = catalogNode.SideEffectLevel,
                ["reason"] = $"執行節點「{catalogNode.DisplayName}」前需要人工批准。"
            };
        }
        run.WorkflowDefinitionJson = root.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}

public sealed record AgentApprovalPolicySnapshot(bool RequiresHumanApproval, string SideEffectLevel, string Reason);
