using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public delegate void AgentRunEventWriter(AgentRun run, AgentRunNode? node, string eventType, string? message, object? payload);

public sealed record AgentNodeExecutionContext(
    EquityLensDbContext DbContext,
    AgentRun Run,
    AgentRunNode Node,
    AgentRunEventWriter AddEvent)
{
    public bool AwaitingApproval { get; private set; }

    public void RequestApproval() => AwaitingApproval = true;
}
