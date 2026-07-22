using System.Text.Json;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Contracts.Agents;

public sealed record AgentWorkflowAdminResponse(
    string WorkflowType,
    string DisplayName,
    string Description,
    string AgentType,
    bool IsEnabled,
    IReadOnlyList<string> NodeTypes,
    IReadOnlyList<AgentWorkflowEdgeResponse> Edges,
    string OrchestrationMode,
    IReadOnlyList<AgentWorkflowNodeResponse> InitialNodes,
    IReadOnlyList<AgentWorkflowEdgeResponse> InitialEdges,
    IReadOnlyList<string> DynamicNodeTypes);
public sealed record AgentWorkflowNodeResponse(string NodeKey, string NodeType);
public sealed record AgentWorkflowEdgeResponse(string From, string To);
public sealed record AgentNodeAdminResponse(string NodeType, string DisplayName, string Description, string Stage, string SideEffectLevel, bool IsEnabled, int TimeoutSeconds, int MaxRetryCount, JsonElement? Metadata, AgentNodeContract Contract, IReadOnlyList<string> RequiredBlackboardKeys, IReadOnlyList<string> ProducedBlackboardKeys, IReadOnlyList<string> AllowedNextNodeTypes);
public sealed record AgentRegistryAdminResponse(IReadOnlyList<WorkflowSkill> Skills, IReadOnlyList<NodeCapability> Capabilities);
public sealed record UpdateAgentWorkflowSettingRequest(bool IsEnabled, string? DisplayName, string? Description);
public sealed record UpdateAgentNodeSettingRequest(bool IsEnabled, string? DisplayName, string? Description, int TimeoutSeconds, int MaxRetryCount, JsonElement? Metadata);
