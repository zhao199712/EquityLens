using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public interface IAgentRunGraphValidator
{
    void Validate(AgentRun run, IReadOnlyList<string> plannedNodeKeys);
}

public sealed class AgentRunGraphValidator : IAgentRunGraphValidator
{
    private readonly IAgentWorkflowCatalog _catalog;

    public AgentRunGraphValidator(IAgentWorkflowCatalog? catalog = null)
    {
        _catalog = catalog ?? new AgentWorkflowCatalog();
    }

    public void Validate(AgentRun run, IReadOnlyList<string> plannedNodeKeys)
    {
        var duplicateNodeKey = run.Nodes
            .GroupBy(node => node.NodeKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateNodeKey is not null)
        {
            throw new InvalidOperationException($"Agent run contains duplicate node key '{duplicateNodeKey}'.");
        }

        var persistedNodeKeys = run.Nodes.Select(node => node.NodeKey).ToHashSet(StringComparer.Ordinal);
        foreach (var plannedNodeKey in plannedNodeKeys)
        {
            if (!persistedNodeKeys.Contains(plannedNodeKey))
            {
                throw new InvalidOperationException($"Agent run is missing node '{plannedNodeKey}'.");
            }
        }

        var plannedNodeKeySet = plannedNodeKeys.ToHashSet(StringComparer.Ordinal);
        var extraNodeKey = persistedNodeKeys.FirstOrDefault(nodeKey => !plannedNodeKeySet.Contains(nodeKey));
        if (extraNodeKey is not null)
        {
            throw new InvalidOperationException($"Agent run contains node '{extraNodeKey}' not present in workflow definition.");
        }

        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var available = blackboard.Where(x => x.Value is not null).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var nodeKey in plannedNodeKeys)
        {
            var node = run.Nodes.Single(x => x.NodeKey == nodeKey);
            if (!_catalog.Nodes.Any(x => x.NodeType == node.NodeType))
            {
                continue;
            }
            var contract = _catalog.GetNode(node.NodeType).Contract;
            var hasPlannedSearchIntents = node.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence
                && !string.IsNullOrWhiteSpace(node.InputJson)
                && node.InputJson.Contains("searchIntents", StringComparison.Ordinal);
            var missing = contract.RequiredBlackboardKeys.FirstOrDefault(x => !(hasPlannedSearchIntents && x == AgentBlackboardKeys.RetrievalPlan) && !available.Contains(x));
            if (missing is not null)
            {
                throw new InvalidOperationException($"Node '{node.NodeType}' requires blackboard key '{missing}' but no preceding node produces it.");
            }
            available.UnionWith(contract.ProducedBlackboardKeys);
        }
    }
}
