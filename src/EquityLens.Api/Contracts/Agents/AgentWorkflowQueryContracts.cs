namespace EquityLens.Api.Contracts.Agents;

public sealed record CreateAgentWorkflowQueryRequest(string Question);

public sealed record AgentWorkflowQueryCreatedResponse(
    Guid AgentRunId,
    Guid? ResearchRunId,
    string WorkflowType,
    string Status,
    string RoutingReason,
    string RoutingModel);
