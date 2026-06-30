using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Deterministic DAG executor for AgentRun workflows.
/// Walks nodes in dependency order, executes each, logs events and tool calls.
/// </summary>
public sealed class AgentWorkflowRunner
{
    private readonly EquityLensDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentWorkflowRunner> _logger;

    public AgentWorkflowRunner(
        EquityLensDbContext db,
        IServiceProvider serviceProvider,
        ILogger<AgentWorkflowRunner> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes all nodes in an AgentRun's workflow DAG.
    /// </summary>
    public async Task ExecuteAsync(AgentRun run, CancellationToken ct)
    {
        var workflowDef = JsonDocument.Parse(run.WorkflowDefinitionJson);
        var nodes = workflowDef.RootElement.GetProperty("nodes");
        var edges = workflowDef.RootElement.GetProperty("edges");

        // Build adjacency: nodeKey -> list of dependent nodeKeys
        var dependents = new Dictionary<string, List<string>>();
        foreach (var edge in edges.EnumerateArray())
        {
            var from = edge.GetProperty("from").GetString()!;
            var to = edge.GetProperty("to").GetString()!;
            if (!dependents.ContainsKey(from))
                dependents[from] = [];
            dependents[from].Add(to);
        }

        // Determine execution order: nodes with no incoming edges first, then follow edges
        var nodeOrder = TopologicalSort(nodes, dependents);

        // Create AgentRunNode records for all nodes
        var nodeEntities = new Dictionary<string, AgentRunNode>();
        foreach (var nodeKey in nodeOrder)
        {
            var nodeDef = nodes.EnumerateArray().First(n => n.GetProperty("id").GetString() == nodeKey);
            var nodeType = nodeDef.GetProperty("type").GetString()!;

            var entity = new AgentRunNode
            {
                AgentRunId = run.Id,
                NodeKey = nodeKey,
                NodeType = nodeType,
                Status = "Pending"
            };
            _db.AgentRunNodes.Add(entity);
            nodeEntities[nodeKey] = entity;
        }
        await _db.SaveChangesAsync(ct);

        // Emit RunCreated + RunStarted events
        await EmitEventAsync(run.Id, null, "RunCreated", "Agent run created.", ct);
        await EmitEventAsync(run.Id, null, "RunStarted", "Agent run execution started.", ct);

        // Mark run as Running
        run.Status = "Running";
        run.StartedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var blackboard = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(run.BlackboardJson))
        {
            var bbDoc = JsonDocument.Parse(run.BlackboardJson);
            blackboard = JsonSerializer.Deserialize<Dictionary<string, object?>>(bbDoc) ?? [];
        }

        // Execute nodes in order
        foreach (var nodeKey in nodeOrder)
        {
            var entity = nodeEntities[nodeKey];

            // Check dependencies: all predecessors must be Succeeded
            var predecessors = edges.EnumerateArray()
                .Where(e => e.GetProperty("to").GetString() == nodeKey)
                .Select(e => e.GetProperty("from").GetString()!)
                .ToList();

            var allPredecessorsSucceeded = predecessors.All(p =>
                nodeEntities.ContainsKey(p) && nodeEntities[p].Status == "Succeeded");

            if (predecessors.Count > 0 && !allPredecessorsSucceeded)
            {
                entity.Status = "Skipped";
                entity.ErrorMessage = "One or more dependencies failed.";
                await EmitEventAsync(run.Id, entity.Id, "NodeSkipped",
                    $"Node '{nodeKey}' skipped due to dependency failure.", ct);
                continue;
            }

            // Mark node as Running
            entity.Status = "Running";
            entity.InputJson = JsonSerializer.Serialize(blackboard);
            entity.StartedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await EmitEventAsync(run.Id, entity.Id, "NodeStarted",
                $"Node '{nodeKey}' ({entity.NodeType}) started.", ct);

            try
            {
                // Resolve and execute the node
                var node = ResolveNode(entity.NodeType);
                var context = new WorkflowNodeContext
                {
                    AgentRunId = run.Id,
                    BlackboardJson = JsonSerializer.Serialize(blackboard),
                    InputJson = run.InputJson,
                    ServiceProvider = _serviceProvider
                };

                var result = await node.ExecuteAsync(context, ct);

                if (result.Success)
                {
                    entity.Status = "Succeeded";
                    entity.OutputJson = result.OutputJson;
                    entity.CompletedAtUtc = DateTime.UtcNow;
                    entity.DurationMs = (long)(entity.CompletedAtUtc.Value - entity.StartedAtUtc!.Value).TotalMilliseconds;

                    // Update blackboard from node output
                    if (!string.IsNullOrEmpty(result.OutputJson))
                    {
                        var outputDoc = JsonDocument.Parse(result.OutputJson);
                        foreach (var prop in outputDoc.RootElement.EnumerateObject())
                        {
                            blackboard[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                        }
                        run.BlackboardJson = JsonSerializer.Serialize(blackboard);
                    }

                    await EmitEventAsync(run.Id, entity.Id, "NodeCompleted",
                        $"Node '{nodeKey}' completed successfully.", ct);
                }
                else
                {
                    entity.Status = "Failed";
                    entity.ErrorMessage = result.ErrorMessage;
                    entity.CompletedAtUtc = DateTime.UtcNow;
                    entity.DurationMs = (long)(entity.CompletedAtUtc.Value - entity.StartedAtUtc!.Value).TotalMilliseconds;

                    await EmitEventAsync(run.Id, entity.Id, "NodeFailed",
                        $"Node '{nodeKey}' failed: {result.ErrorMessage}", ct);

                    // Mark run as Failed
                    run.Status = "Failed";
                    run.ErrorMessage = $"Node '{nodeKey}' failed: {result.ErrorMessage}";
                    run.CompletedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    await EmitEventAsync(run.Id, null, "RunFailed",
                        $"Agent run failed at node '{nodeKey}'.", ct);
                    return;
                }

                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in node {NodeKey}", nodeKey);
                entity.Status = "Failed";
                entity.ErrorMessage = ex.Message;
                entity.CompletedAtUtc = DateTime.UtcNow;
                entity.DurationMs = (long)(entity.CompletedAtUtc.Value - entity.StartedAtUtc!.Value).TotalMilliseconds;

                await EmitEventAsync(run.Id, entity.Id, "NodeFailed",
                    $"Node '{nodeKey}' threw exception: {ex.Message}", ct);

                run.Status = "Failed";
                run.ErrorMessage = $"Node '{nodeKey}' threw: {ex.Message}";
                run.CompletedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                await EmitEventAsync(run.Id, null, "RunFailed",
                    $"Agent run failed at node '{nodeKey}': {ex.Message}", ct);
                return;
            }
        }

        // All nodes succeeded
        run.Status = "Succeeded";
        run.CompletedAtUtc = DateTime.UtcNow;
        run.OutputJson = blackboard.ContainsKey("finalOutput")
            ? JsonSerializer.Serialize(blackboard["finalOutput"])
            : null;
        await _db.SaveChangesAsync(ct);

        await EmitEventAsync(run.Id, null, "RunSucceeded",
            "Agent run completed successfully.", ct);
    }

    private IWorkflowNode ResolveNode(string nodeType)
    {
        var nodes = _serviceProvider.GetServices<IWorkflowNode>();
        var node = nodes.FirstOrDefault(n => n.NodeType == nodeType);
        if (node is null)
            throw new InvalidOperationException($"No IWorkflowNode registered for type '{nodeType}'.");
        return node;
    }

    private async Task EmitEventAsync(
        Guid runId, Guid? nodeId, string eventType, string? message, CancellationToken ct)
    {
        var evt = new AgentRunEvent
        {
            AgentRunId = runId,
            AgentRunNodeId = nodeId,
            EventType = eventType,
            Message = message
        };
        _db.AgentRunEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }

    private static List<string> TopologicalSort(JsonElement nodes, Dictionary<string, List<string>> dependents)
    {
        var nodeIds = nodes.EnumerateArray()
            .Select(n => n.GetProperty("id").GetString()!)
            .ToList();

        // Build in-degree map
        var inDegree = nodeIds.ToDictionary(id => id, _ => 0);
        foreach (var (_, deps) in dependents)
        {
            foreach (var dep in deps)
            {
                if (inDegree.ContainsKey(dep))
                    inDegree[dep]++;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            if (dependents.TryGetValue(current, out var next))
            {
                foreach (var n in next)
                {
                    inDegree[n]--;
                    if (inDegree[n] == 0)
                        queue.Enqueue(n);
                }
            }
        }

        return result;
    }
}
