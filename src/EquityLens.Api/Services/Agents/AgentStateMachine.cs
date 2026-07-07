using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public interface IAgentRunStateMachine
{
    void Transition(AgentRun run, string nextStatus);

    void ResetForRetry(AgentRun run);
}

public interface IAgentNodeStateMachine
{
    void Transition(AgentRunNode node, string nextStatus);

    void ResetForRetry(AgentRunNode node);
}

public sealed class AgentRunStateMachine : IAgentRunStateMachine
{
    public void Transition(AgentRun run, string nextStatus)
    {
        if (!CanTransition(run.Status, nextStatus))
        {
            throw new InvalidOperationException($"Invalid agent run status transition: {run.Status} -> {nextStatus}.");
        }

        run.Status = nextStatus;
    }

    public void ResetForRetry(AgentRun run)
    {
        if (!string.Equals(run.Status, AgentRunStatuses.Failed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid agent run retry reset from status: {run.Status}.");
        }

        run.Status = AgentRunStatuses.Pending;
    }

    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        (AgentRunStatuses.Pending, AgentRunStatuses.Running) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Succeeded) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Failed) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Cancelled) => true,
        (AgentRunStatuses.Pending, AgentRunStatuses.Cancelled) => true,
        (AgentRunStatuses.Failed, AgentRunStatuses.Cancelled) => true,
        _ => false
    };
}

public sealed class AgentNodeStateMachine : IAgentNodeStateMachine
{
    public void Transition(AgentRunNode node, string nextStatus)
    {
        if (!CanTransition(node.Status, nextStatus))
        {
            throw new InvalidOperationException($"Invalid agent node status transition: {node.Status} -> {nextStatus}.");
        }

        node.Status = nextStatus;
    }

    public void ResetForRetry(AgentRunNode node)
    {
        if (string.Equals(node.Status, AgentNodeStatuses.Pending, StringComparison.Ordinal))
        {
            return;
        }

        if (node.Status is not (AgentNodeStatuses.Ready or AgentNodeStatuses.Running or AgentNodeStatuses.Succeeded or AgentNodeStatuses.Failed))
        {
            throw new InvalidOperationException($"Invalid agent node retry reset from status: {node.Status}.");
        }

        node.Status = AgentNodeStatuses.Pending;
    }

    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        (AgentNodeStatuses.Pending, AgentNodeStatuses.Ready) => true,
        (AgentNodeStatuses.Ready, AgentNodeStatuses.Running) => true,
        (AgentNodeStatuses.Running, AgentNodeStatuses.Succeeded) => true,
        (AgentNodeStatuses.Running, AgentNodeStatuses.Failed) => true,
        _ => false
    };
}
