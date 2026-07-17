using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

public sealed class AgentWorkflowAdminService(EquityLensDbContext db, IAgentWorkflowCatalog catalog) : IAgentWorkflowAdminService
{
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
        if (request.TimeoutSeconds is < 1 or > 3600 || request.MaxRetryCount is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(request));
        var entry = catalog.Nodes.SingleOrDefault(x => x.NodeType == type); if (entry is null) return null;
        var setting = await db.AgentNodeSettings.SingleOrDefaultAsync(x => x.NodeType == type, ct) ?? new AgentNodeSetting { Id = Guid.NewGuid(), NodeType = type };
        setting.IsEnabled = request.IsEnabled; setting.DisplayName = request.DisplayName?.Trim(); setting.Description = request.Description?.Trim(); setting.TimeoutSeconds = request.TimeoutSeconds; setting.MaxRetryCount = request.MaxRetryCount; setting.UpdatedAtUtc = DateTime.UtcNow;
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
    private static AgentWorkflowAdminResponse Map(AgentWorkflowCatalogEntry x, AgentWorkflowSetting? s) => new(x.WorkflowType, s?.DisplayName ?? x.DisplayName, s?.Description ?? x.Description, x.AgentType, s?.IsEnabled ?? true, x.NodeTypes, x.Edges.Select(e => new AgentWorkflowEdgeResponse(e.From, e.To)).ToList());
    private static AgentNodeAdminResponse Map(AgentNodeCatalogEntry x, AgentNodeSetting? s) => new(x.NodeType, s?.DisplayName ?? x.DisplayName, s?.Description ?? x.Description, x.Stage, x.SideEffectLevel, s?.IsEnabled ?? true, s?.TimeoutSeconds ?? x.DefaultPolicy.TimeoutSeconds, s?.MaxRetryCount ?? x.DefaultPolicy.MaxRetryCount, x.RequiredBlackboardKeys, x.ProducedBlackboardKeys, x.AllowedNextNodeTypes);
}
