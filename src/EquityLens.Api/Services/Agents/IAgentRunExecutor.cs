namespace EquityLens.Api.Services.Agents;

public enum AgentRunExecutionOutcome { Executed, SkippedTerminalOrWaiting, LeaseHeldByOther, LeaseLostDuringExecution }

public interface IAgentRunExecutor
{
    Task<AgentRunExecutionOutcome> ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default);
}
