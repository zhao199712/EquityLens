using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

/// <summary>只有目前註冊版本可繼續執行；歷史定義保持原樣供唯讀查詢。</summary>
public sealed class WorkflowExecutionCompatibility
{
    private readonly Dictionary<string, int> versions;
    private readonly HashSet<string> nodeTypes;

    public WorkflowExecutionCompatibility(IEnumerable<IAgentWorkflowDefinitionProvider> providers, IAgentWorkflowCatalog catalog)
    {
        versions = providers.ToDictionary(p => p.WorkflowType,
            p => JsonNode.Parse(p.CreateRun(Guid.Empty, Guid.Empty).WorkflowDefinitionJson)?["version"]?.GetValue<int>() ?? 1);
        nodeTypes = catalog.Nodes.Select(n => n.NodeType).ToHashSet(StringComparer.Ordinal);
    }

    public bool IsRetired(AgentRun run)
    {
        if (!versions.TryGetValue(run.WorkflowType, out var version)) return true;
        try
        {
            var definition = JsonNode.Parse(run.WorkflowDefinitionJson)!;
            if ((definition["version"]?.GetValue<int>() ?? 1) != version) return true;
            if (definition["workflowType"]?.GetValue<string>() != run.WorkflowType) return true;
            return run.Nodes.Any(n => !nodeTypes.Contains(n.NodeType))
                || definition["nodes"]!.AsArray().Any(n => !nodeTypes.Contains(n!["type"]!.GetValue<string>()));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException)
        {
            return true;
        }
    }

    public void EnsureExecutable(AgentRun run)
    {
        if (IsRetired(run)) throw new LegacyWorkflowRetiredException();
    }
}

public sealed class LegacyWorkflowRetiredException() : Exception("此版本工作流已停用，歷史紀錄僅供查閱，請建立新的分析。")
{
    public const string Code = "legacy_workflow_retired";
}
