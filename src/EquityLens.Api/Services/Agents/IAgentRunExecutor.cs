namespace EquityLens.Api.Services.Agents;

public interface IAgentRunExecutor
{
    Task ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default);
}
