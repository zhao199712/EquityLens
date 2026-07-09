namespace EquityLens.Api.Services.Agents;

public sealed record AgentRunQueueMessage(
    Guid RunId,
    Guid UserId,
    string WorkflowType,
    DateTime EnqueuedAtUtc);

public sealed record AgentRunQueueItem(
    string StreamId,
    AgentRunQueueMessage Message);

public interface IAgentRunQueue
{
    Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default);

    Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default);

    Task<AgentRunQueueItem?> ReadStalePendingAsync(
        string consumerName,
        TimeSpan minIdleTime,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default);
}
