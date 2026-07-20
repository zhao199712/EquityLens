using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EquityLens.Api.Services.Agents;

public interface IAgentWorkflowAdminService
{
    Task<IReadOnlyList<AgentWorkflowAdminResponse>> ListWorkflowsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentNodeAdminResponse>> ListNodesAsync(CancellationToken ct = default);
    Task<AgentWorkflowAdminResponse?> UpdateWorkflowAsync(string type, UpdateAgentWorkflowSettingRequest request, CancellationToken ct = default);
    Task<AgentNodeAdminResponse?> UpdateNodeAsync(string type, UpdateAgentNodeSettingRequest request, CancellationToken ct = default);
    Task EnsureEnabledAsync(string workflowType, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, AgentNodeExecutionPolicy>> GetPoliciesAsync(IEnumerable<string> nodeTypes, CancellationToken ct = default);
}

public sealed class AgentWorkflowAdminService : IAgentWorkflowAdminService
{
    private readonly EquityLensDbContext db;
    private readonly IAgentWorkflowCatalog catalog;
    private readonly IReadOnlyDictionary<string, IAgentWorkflowDefinitionProvider> providers;
    private readonly INodeCapabilityRegistry? capabilities;

    public AgentWorkflowAdminService(
        EquityLensDbContext db,
        IAgentWorkflowCatalog catalog,
        IEnumerable<IAgentWorkflowDefinitionProvider>? providers = null,
        INodeCapabilityRegistry? capabilities = null)
    {
        this.db = db;
        this.catalog = catalog;
        this.providers = (providers ?? []).ToDictionary(x => x.WorkflowType, StringComparer.Ordinal);
        this.capabilities = capabilities;
    }

    public async Task<IReadOnlyList<AgentWorkflowAdminResponse>> ListWorkflowsAsync(CancellationToken ct = default)
    {
        var settings = await db.AgentWorkflowSettings.AsNoTracking().ToDictionaryAsync(x => x.WorkflowType, ct);
        return catalog.Workflows.Select(x => Map(x, settings.GetValueOrDefault(x.WorkflowType))).ToList();
    }
    public async Task<IReadOnlyList<AgentNodeAdminResponse>> ListNodesAsync(CancellationToken ct = default)
    {
        var settings = await db.AgentNodeSettings.AsNoTracking().ToDictionaryAsync(x => x.NodeType, ct);
        return catalog.Nodes.Select(x => Map(x, settings.GetValueOrDefault(x.NodeType))).ToList();
    }
    public async Task<AgentWorkflowAdminResponse?> UpdateWorkflowAsync(string type, UpdateAgentWorkflowSettingRequest request, CancellationToken ct = default)
    {
        var entry = catalog.Workflows.SingleOrDefault(x => x.WorkflowType == type); if (entry is null) return null;
        var setting = await db.AgentWorkflowSettings.SingleOrDefaultAsync(x => x.WorkflowType == type, ct) ?? new AgentWorkflowSetting { Id = Guid.NewGuid(), WorkflowType = type };
        setting.IsEnabled = request.IsEnabled; setting.DisplayName = request.DisplayName?.Trim(); setting.Description = request.Description?.Trim(); setting.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(setting).State == EntityState.Detached) db.AgentWorkflowSettings.Add(setting);
        await db.SaveChangesAsync(ct); return Map(entry, setting);
    }
    public async Task<AgentNodeAdminResponse?> UpdateNodeAsync(string type, UpdateAgentNodeSettingRequest request, CancellationToken ct = default)
    {
        if (request.TimeoutSeconds is < 1 or > 3600 || request.MaxRetryCount is < 0 or > 5 || request.Metadata is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null }) throw new ArgumentOutOfRangeException(nameof(request));
        var entry = catalog.Nodes.SingleOrDefault(x => x.NodeType == type); if (entry is null) return null;
        var setting = await db.AgentNodeSettings.SingleOrDefaultAsync(x => x.NodeType == type, ct) ?? new AgentNodeSetting { Id = Guid.NewGuid(), NodeType = type };
        setting.IsEnabled = request.IsEnabled; setting.DisplayName = request.DisplayName?.Trim(); setting.Description = request.Description?.Trim(); setting.MetadataJson = request.Metadata?.ValueKind == JsonValueKind.Null ? null : request.Metadata?.GetRawText(); setting.TimeoutSeconds = request.TimeoutSeconds; setting.MaxRetryCount = request.MaxRetryCount; setting.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(setting).State == EntityState.Detached) db.AgentNodeSettings.Add(setting);
        await db.SaveChangesAsync(ct); return Map(entry, setting);
    }
    public async Task EnsureEnabledAsync(string workflowType, CancellationToken ct = default)
    {
        var workflow = catalog.GetWorkflow(workflowType);
        if (await db.AgentWorkflowSettings.AnyAsync(x => x.WorkflowType == workflowType && !x.IsEnabled, ct)) throw new InvalidOperationException($"Workflow '{workflowType}' is disabled.");
        var disabled = await db.AgentNodeSettings.Where(x => workflow.NodeTypes.Contains(x.NodeType) && !x.IsEnabled).Select(x => x.NodeType).FirstOrDefaultAsync(ct);
        if (disabled is not null) throw new InvalidOperationException($"Node '{disabled}' is disabled.");
    }
    public async Task<IReadOnlyDictionary<string, AgentNodeExecutionPolicy>> GetPoliciesAsync(IEnumerable<string> nodeTypes, CancellationToken ct = default)
    {
        var types = nodeTypes.Distinct().ToArray(); var settings = await db.AgentNodeSettings.Where(x => types.Contains(x.NodeType)).ToDictionaryAsync(x => x.NodeType, ct);
        return types.ToDictionary(x => x, x => { var d = catalog.GetNode(x).DefaultPolicy; var s = settings.GetValueOrDefault(x); return new AgentNodeExecutionPolicy(s?.TimeoutSeconds ?? d.TimeoutSeconds, s?.MaxRetryCount ?? d.MaxRetryCount); });
    }
    private AgentWorkflowAdminResponse Map(AgentWorkflowCatalogEntry x, AgentWorkflowSetting? s)
    {
        if (!providers.TryGetValue(x.WorkflowType, out var provider))
        {
            return new(x.WorkflowType, s?.DisplayName ?? x.DisplayName, s?.Description ?? x.Description, x.AgentType, s?.IsEnabled ?? true,
                x.NodeTypes, x.Edges.Select(e => new AgentWorkflowEdgeResponse(e.From, e.To)).ToList(), "Static",
                x.NodeTypes.Select(type => new AgentWorkflowNodeResponse(type, type)).ToList(),
                x.Edges.Select(e => new AgentWorkflowEdgeResponse(e.From, e.To)).ToList(), []);
        }

        var run = provider.CreateRun(Guid.Empty, Guid.Empty);
        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        var root = definition.RootElement;
        var mode = root.TryGetProperty("orchestrationMode", out var modeElement)
            ? modeElement.GetString() ?? "Static"
            : "Static";
        var initialNodes = root.GetProperty("nodes").EnumerateArray()
            .Select(node => new AgentWorkflowNodeResponse(
                node.GetProperty("id").GetString() ?? throw new InvalidOperationException("Workflow node id is missing."),
                node.GetProperty("type").GetString() ?? throw new InvalidOperationException("Workflow node type is missing.")))
            .ToList();
        var initialEdges = root.GetProperty("edges").EnumerateArray()
            .Select(edge => new AgentWorkflowEdgeResponse(
                edge.GetProperty("from").GetString() ?? throw new InvalidOperationException("Workflow edge from is missing."),
                edge.GetProperty("to").GetString() ?? throw new InvalidOperationException("Workflow edge to is missing.")))
            .ToList();
        var dynamicNodeTypes = mode == "DynamicStateful" && capabilities is not null
            ? capabilities.Capabilities.Select(capability => capability.NodeType).Distinct(StringComparer.Ordinal).ToList()
            : [];

        return new(x.WorkflowType, s?.DisplayName ?? x.DisplayName, s?.Description ?? x.Description, x.AgentType, s?.IsEnabled ?? true,
            x.NodeTypes, x.Edges.Select(e => new AgentWorkflowEdgeResponse(e.From, e.To)).ToList(), mode, initialNodes, initialEdges, dynamicNodeTypes);
    }
    private static AgentNodeAdminResponse Map(AgentNodeCatalogEntry x, AgentNodeSetting? s) => new(x.NodeType, s?.DisplayName ?? x.DisplayName, s?.Description ?? x.Description, x.Stage, x.SideEffectLevel, s?.IsEnabled ?? true, s?.TimeoutSeconds ?? x.DefaultPolicy.TimeoutSeconds, s?.MaxRetryCount ?? x.DefaultPolicy.MaxRetryCount, ParseMetadata(s?.MetadataJson), x.Contract, x.RequiredBlackboardKeys, x.ProducedBlackboardKeys, x.AllowedNextNodeTypes);
    private static JsonElement? ParseMetadata(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonDocument.Parse(value).RootElement.Clone();
}
