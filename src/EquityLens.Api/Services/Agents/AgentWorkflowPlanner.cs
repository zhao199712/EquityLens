using System.Text.Json;

namespace EquityLens.Api.Services.Agents;

public interface IWorkflowGraphTopologyService
{
    IReadOnlyList<string> GetExecutionOrder(string workflowDefinitionJson);
}

public class WorkflowGraphTopologyService : IWorkflowGraphTopologyService
{
    public IReadOnlyList<string> GetExecutionOrder(string workflowDefinitionJson)
    {
        using var document = JsonDocument.Parse(workflowDefinitionJson);
        var root = document.RootElement;
        var nodes = root.GetProperty("nodes")
            .EnumerateArray()
            .Select(node => node.GetProperty("id").GetString() ?? throw new InvalidOperationException("Workflow node id is missing."))
            .ToList();

        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("Workflow definition has no nodes.");
        }

        var nodeSet = nodes.ToHashSet(StringComparer.Ordinal);
        if (nodeSet.Count != nodes.Count)
        {
            throw new InvalidOperationException("Workflow definition contains duplicate node ids.");
        }

        var outgoing = nodes.ToDictionary(node => node, _ => new List<string>(), StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(node => node, _ => 0, StringComparer.Ordinal);

        foreach (var edge in root.GetProperty("edges").EnumerateArray())
        {
            var from = edge.GetProperty("from").GetString() ?? throw new InvalidOperationException("Workflow edge from is missing.");
            var to = edge.GetProperty("to").GetString() ?? throw new InvalidOperationException("Workflow edge to is missing.");
            if (!nodeSet.Contains(from))
            {
                throw new InvalidOperationException($"Workflow edge references unknown from node '{from}'.");
            }
            if (!nodeSet.Contains(to))
            {
                throw new InvalidOperationException($"Workflow edge references unknown to node '{to}'.");
            }

            outgoing[from].Add(to);
            indegree[to]++;
        }

        var ready = new Queue<string>(nodes.Where(node => indegree[node] == 0));
        var order = new List<string>(nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            order.Add(node);
            foreach (var next in outgoing[node])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        if (order.Count != nodes.Count)
        {
            throw new InvalidOperationException("Workflow definition contains a cycle.");
        }

        return order;
    }
}

/// <summary>Compatibility facade for callers that used the old, misleading planner name.</summary>
public sealed class AgentWorkflowPlanner : WorkflowGraphTopologyService;
