using System.Diagnostics;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;

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
        var currentStatus = run.Status;
        if (!CanTransition(currentStatus, nextStatus))
        {
            throw new InvalidOperationException($"Invalid agent run status transition: {currentStatus} -> {nextStatus}.");
        }

        run.Status = nextStatus;
        RecordRunTransition(run, currentStatus, nextStatus, reset: false);
    }

    public void ResetForRetry(AgentRun run)
    {
        var currentStatus = run.Status;
        if (!string.Equals(currentStatus, AgentRunStatuses.Failed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid agent run retry reset from status: {currentStatus}.");
        }

        run.Status = AgentRunStatuses.Pending;
        RecordRunTransition(run, currentStatus, AgentRunStatuses.Pending, reset: true);
    }

    private static void RecordRunTransition(AgentRun run, string fromStatus, string toStatus, bool reset)
    {
        EquityLensTelemetry.AgentRunStatusTransitions.Add(1,
            new KeyValuePair<string, object?>("workflow.type", run.WorkflowType),
            new KeyValuePair<string, object?>("agent.type", run.AgentType),
            new KeyValuePair<string, object?>("status.from", fromStatus),
            new KeyValuePair<string, object?>("status.to", toStatus),
            new KeyValuePair<string, object?>("transition.reset", reset));

        Activity.Current?.AddEvent(new ActivityEvent(
            reset ? "agent.run.status.reset" : "agent.run.status.transition",
            tags: new ActivityTagsCollection
            {
                ["agent.run.id"] = run.Id,
                ["workflow.type"] = run.WorkflowType,
                ["agent.type"] = run.AgentType,
                ["status.from"] = fromStatus,
                ["status.to"] = toStatus,
                ["transition.reset"] = reset
            }));
    }

    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        (AgentRunStatuses.Pending, AgentRunStatuses.Running) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Succeeded) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Failed) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.Cancelled) => true,
        (AgentRunStatuses.Running, AgentRunStatuses.WaitingForFeedback) => true,
        (AgentRunStatuses.WaitingForFeedback, AgentRunStatuses.Running) => true,
        (AgentRunStatuses.WaitingForFeedback, AgentRunStatuses.Failed) => true,
        (AgentRunStatuses.WaitingForFeedback, AgentRunStatuses.Cancelled) => true,
        (AgentRunStatuses.Pending, AgentRunStatuses.Cancelled) => true,
        (AgentRunStatuses.Failed, AgentRunStatuses.Cancelled) => true,
        _ => false
    };
}

public sealed class AgentNodeStateMachine : IAgentNodeStateMachine
{
    public void Transition(AgentRunNode node, string nextStatus)
    {
        var currentStatus = node.Status;
        if (!CanTransition(currentStatus, nextStatus))
        {
            throw new InvalidOperationException($"Invalid agent node status transition: {currentStatus} -> {nextStatus}.");
        }

        node.Status = nextStatus;
        RecordNodeTransition(node, currentStatus, nextStatus, reset: false);
    }

    public void ResetForRetry(AgentRunNode node)
    {
        var currentStatus = node.Status;
        if (string.Equals(currentStatus, AgentNodeStatuses.Pending, StringComparison.Ordinal))
        {
            return;
        }

        if (currentStatus is not (AgentNodeStatuses.Ready or AgentNodeStatuses.Queued or AgentNodeStatuses.Running or AgentNodeStatuses.Succeeded or AgentNodeStatuses.Failed))
        {
            throw new InvalidOperationException($"Invalid agent node retry reset from status: {currentStatus}.");
        }

        node.Status = AgentNodeStatuses.Pending;
        RecordNodeTransition(node, currentStatus, AgentNodeStatuses.Pending, reset: true);
    }

    private static void RecordNodeTransition(AgentRunNode node, string fromStatus, string toStatus, bool reset)
    {
        EquityLensTelemetry.AgentNodeStatusTransitions.Add(1,
            new KeyValuePair<string, object?>("node.type", node.NodeType),
            new KeyValuePair<string, object?>("node.key", node.NodeKey),
            new KeyValuePair<string, object?>("status.from", fromStatus),
            new KeyValuePair<string, object?>("status.to", toStatus),
            new KeyValuePair<string, object?>("transition.reset", reset));

        Activity.Current?.AddEvent(new ActivityEvent(
            reset ? "agent.node.status.reset" : "agent.node.status.transition",
            tags: new ActivityTagsCollection
            {
                ["agent.run.id"] = node.AgentRunId,
                ["agent.run.node.id"] = node.Id,
                ["node.key"] = node.NodeKey,
                ["node.type"] = node.NodeType,
                ["status.from"] = fromStatus,
                ["status.to"] = toStatus,
                ["transition.reset"] = reset
            }));
    }

    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        (AgentNodeStatuses.Pending, AgentNodeStatuses.Ready) => true,
        (AgentNodeStatuses.Ready, AgentNodeStatuses.Queued) => true,
        (AgentNodeStatuses.Ready, AgentNodeStatuses.Running) => true,
        (AgentNodeStatuses.Queued, AgentNodeStatuses.Running) => true,
        (AgentNodeStatuses.Running, AgentNodeStatuses.Succeeded) => true,
        (AgentNodeStatuses.Running, AgentNodeStatuses.Failed) => true,
        (AgentNodeStatuses.Running, AgentNodeStatuses.WaitingForFeedback) => true,
        (AgentNodeStatuses.WaitingForFeedback, AgentNodeStatuses.Pending) => true,
        (AgentNodeStatuses.WaitingForFeedback, AgentNodeStatuses.Failed) => true,
        (AgentNodeStatuses.WaitingForFeedback, AgentNodeStatuses.Cancelled) => true,
        _ => false
    };
}
