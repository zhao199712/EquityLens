namespace EquityLens.Api.Services.Agents;

public sealed record NodeMetadata(
    string NodeType,
    string Stage,
    IReadOnlyList<string> RequiredBlackboardKeys,
    IReadOnlyList<string> ProducedBlackboardKeys);

public static class AgentNodeMetadata
{
    private static readonly IReadOnlyDictionary<string, NodeMetadata> ByNodeType =
        new AgentWorkflowCatalog().Nodes.ToDictionary(
            x => x.NodeType,
            x => new NodeMetadata(x.NodeType, x.Stage, x.Contract.RequiredBlackboardKeys, x.Contract.ProducedBlackboardKeys),
            StringComparer.Ordinal);

    public static NodeMetadata? GetByNodeType(string nodeType) =>
        ByNodeType.TryGetValue(nodeType, out var metadata) ? metadata : null;

    public static IReadOnlyDictionary<string, NodeMetadata> GetAll() => ByNodeType;
}

public static class NodeMetadataValidator
{
    public static void ValidateBlackboardDependencies(
        IReadOnlyList<string> executionOrder,
        string initialBlackboardJson,
        IReadOnlyDictionary<string, NodeMetadata> metadataRegistry)
    {
        var blackboard = AgentNodeJson.ParseBlackboard(initialBlackboardJson);
        var producedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeKey in executionOrder)
        {
            var metadata = metadataRegistry.Values.FirstOrDefault(m =>
                string.Equals(m.NodeType, nodeKey, StringComparison.Ordinal));
            if (metadata is null)
            {
                continue;
            }

            foreach (var requiredKey in metadata.RequiredBlackboardKeys)
            {
                var inInitialBlackboard = blackboard.ContainsKey(requiredKey) && blackboard[requiredKey] is not null;
                var producedByPreviousNode = producedKeys.Contains(requiredKey);
                if (!inInitialBlackboard && !producedByPreviousNode)
                {
                    throw new InvalidOperationException(
                        $"Node '{nodeKey}' requires blackboard key '{requiredKey}' but it is not present. " +
                        $"Ensure a previous node produces this key or it is in the initial blackboard.");
                }
            }

            foreach (var producedKey in metadata.ProducedBlackboardKeys)
            {
                producedKeys.Add(producedKey);
            }
        }
    }
}
