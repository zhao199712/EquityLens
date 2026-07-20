using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunWakeOutboxDispatcher(IServiceScopeFactory scopes, ILogger<AgentRunWakeOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>(); var queue = scope.ServiceProvider.GetRequiredService<IAgentRunQueue>();
                var items = await db.AgentRunWakeOutbox.Where(x => x.PublishedAtUtc == null).OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(stoppingToken);
                foreach (var item in items)
                {
                    await queue.EnqueueAsync(new AgentRunQueueMessage(item.AgentRunId, item.UserId, item.WorkflowType, DateTime.UtcNow), stoppingToken);
                    item.PublishedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(stoppingToken);
                }
                if (items.Count == 0) await Task.Delay(250, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Agent run wake outbox dispatch failed."); await Task.Delay(1000, stoppingToken); }
        }
    }
}
