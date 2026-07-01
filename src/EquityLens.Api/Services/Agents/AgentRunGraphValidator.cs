using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public interface IAgentRunGraphValidator
{
    void Validate(AgentRun run, IReadOnlyList<string> plannedNodeKeys);
}

public sealed class AgentRunGraphValidator : IAgentRunGraphValidator
{
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
    }
}
