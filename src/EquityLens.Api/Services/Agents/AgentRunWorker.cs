using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunWorker> _logger;
    private readonly TimeSpan _pendingMessageMinIdleTime;
    private readonly string _consumerName;

    public AgentRunWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRunWorker> logger,
        IOptions<AgentRunQueueOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pendingMessageMinIdleTime = TimeSpan.FromSeconds(Math.Max(1, options.Value.PendingMinIdleSeconds));
        _consumerName = $"agent-run-worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IAgentRunQueue>();
        var executor = scope.ServiceProvider.GetRequiredService<IAgentRunExecutor>();

        var item = await queue.ReadStalePendingAsync(_consumerName, _pendingMessageMinIdleTime, cancellationToken)
            ?? await queue.ReadNextAsync(_consumerName, cancellationToken);
        if (item is null)
        {
            return false;
        }

        await executor.ExecuteAsync(item.Message.RunId, item.Message.UserId, cancellationToken);
        await queue.AcknowledgeAsync(item.StreamId, cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Agent run worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
