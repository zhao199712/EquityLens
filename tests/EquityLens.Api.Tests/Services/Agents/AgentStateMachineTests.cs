using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentStateMachineTests
{
    [Theory]
    [InlineData(AgentRunStatuses.Pending, AgentRunStatuses.Running)]
    [InlineData(AgentRunStatuses.Running, AgentRunStatuses.Succeeded)]
    [InlineData(AgentRunStatuses.Running, AgentRunStatuses.Failed)]
    [InlineData(AgentRunStatuses.Running, AgentRunStatuses.Cancelled)]
    [InlineData(AgentRunStatuses.Pending, AgentRunStatuses.Cancelled)]
    [InlineData(AgentRunStatuses.Failed, AgentRunStatuses.Cancelled)]
    public void AgentRunStateMachine_Transition_AllowsValidTransitions(string current, string next)
    {
        var run = new AgentRun { Status = current };
        var stateMachine = new AgentRunStateMachine();

        stateMachine.Transition(run, next);

        Assert.Equal(next, run.Status);
    }

    [Theory]
    [InlineData(AgentRunStatuses.Pending, AgentRunStatuses.Succeeded)]
    [InlineData(AgentRunStatuses.Succeeded, AgentRunStatuses.Running)]
    [InlineData(AgentRunStatuses.Cancelled, AgentRunStatuses.Running)]
    [InlineData(AgentRunStatuses.Failed, AgentRunStatuses.Running)]
    public void AgentRunStateMachine_Transition_RejectsInvalidTransitions(string current, string next)
    {
        var run = new AgentRun { Status = current };
        var stateMachine = new AgentRunStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(() => stateMachine.Transition(run, next));

        Assert.Equal($"Invalid agent run status transition: {current} -> {next}.", exception.Message);
        Assert.Equal(current, run.Status);
    }

    [Fact]
    public void AgentRunStateMachine_ResetForRetry_ResetsFailedRunToPending()
    {
        var run = new AgentRun { Status = AgentRunStatuses.Failed };
        var stateMachine = new AgentRunStateMachine();

        stateMachine.ResetForRetry(run);

        Assert.Equal(AgentRunStatuses.Pending, run.Status);
    }

    [Fact]
    public void AgentRunStateMachine_ResetForRetry_RejectsNonFailedRun()
    {
        var run = new AgentRun { Status = AgentRunStatuses.Running };
        var stateMachine = new AgentRunStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(() => stateMachine.ResetForRetry(run));

        Assert.Equal($"Invalid agent run retry reset from status: {AgentRunStatuses.Running}.", exception.Message);
    }

    [Theory]
    [InlineData(AgentNodeStatuses.Pending, AgentNodeStatuses.Ready)]
    [InlineData(AgentNodeStatuses.Ready, AgentNodeStatuses.Running)]
    [InlineData(AgentNodeStatuses.Running, AgentNodeStatuses.Succeeded)]
    [InlineData(AgentNodeStatuses.Running, AgentNodeStatuses.Failed)]
    public void AgentNodeStateMachine_Transition_AllowsValidTransitions(string current, string next)
    {
        var node = new AgentRunNode { Status = current };
        var stateMachine = new AgentNodeStateMachine();

        stateMachine.Transition(node, next);

        Assert.Equal(next, node.Status);
    }

    [Theory]
    [InlineData(AgentNodeStatuses.Pending, AgentNodeStatuses.Succeeded)]
    [InlineData(AgentNodeStatuses.Ready, AgentNodeStatuses.Succeeded)]
    [InlineData(AgentNodeStatuses.Succeeded, AgentNodeStatuses.Running)]
    [InlineData(AgentNodeStatuses.Failed, AgentNodeStatuses.Running)]
    public void AgentNodeStateMachine_Transition_RejectsInvalidTransitions(string current, string next)
    {
        var node = new AgentRunNode { Status = current };
        var stateMachine = new AgentNodeStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(() => stateMachine.Transition(node, next));

        Assert.Equal($"Invalid agent node status transition: {current} -> {next}.", exception.Message);
        Assert.Equal(current, node.Status);
    }

    [Theory]
    [InlineData(AgentNodeStatuses.Pending)]
    [InlineData(AgentNodeStatuses.Ready)]
    [InlineData(AgentNodeStatuses.Running)]
    [InlineData(AgentNodeStatuses.Succeeded)]
    [InlineData(AgentNodeStatuses.Failed)]
    public void AgentNodeStateMachine_ResetForRetry_ResetsRetryableNodeToPending(string current)
    {
        var node = new AgentRunNode { Status = current };
        var stateMachine = new AgentNodeStateMachine();

        stateMachine.ResetForRetry(node);

        Assert.Equal(AgentNodeStatuses.Pending, node.Status);
    }

    [Theory]
    [InlineData(AgentNodeStatuses.Skipped)]
    [InlineData(AgentNodeStatuses.WaitingForFeedback)]
    public void AgentNodeStateMachine_ResetForRetry_RejectsUnsupportedNodeStatus(string current)
    {
        var node = new AgentRunNode { Status = current };
        var stateMachine = new AgentNodeStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(() => stateMachine.ResetForRetry(node));

        Assert.Equal($"Invalid agent node retry reset from status: {current}.", exception.Message);
    }
}
