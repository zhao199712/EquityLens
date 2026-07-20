using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.AdminJobs;
using EquityLens.Api.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.BackgroundWorkers;

/// <summary>
/// 通用背景工作 Worker，從 Redis Stream 讀取資料匯入工作並執行。
/// </summary>
public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundJobWorker> _logger;
    private readonly string _consumerName;

    public BackgroundJobWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundJobWorker> logger,
        IOptions<RedisOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _consumerName = $"background-job-worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Background job worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<IBackgroundJobExecutor>();
        var item = await queue.ReadNextAsync(_consumerName, cancellationToken);
        if (item is null) return false;

        var jobRun = await dbContext.JobRuns.FindAsync(item.Job.JobId, cancellationToken);
        if (jobRun is null)
        {
            _logger.LogWarning("Job run {JobId} not found, acking stream message.", item.Job.JobId);
            await queue.AcknowledgeAsync(item.StreamId, cancellationToken);
            return true;
        }
        if (jobRun.Status == "Cancelled")
        {
            jobRun.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await queue.AcknowledgeAsync(item.StreamId, cancellationToken);
            return true;
        }

        jobRun.Status = "Running";
        jobRun.StartedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        var result = await executor.ExecuteAsync(jobRun, cancellationToken);

        // Cancel endpoint may have changed the row while a long-running sync was processing.
        await dbContext.Entry(jobRun).ReloadAsync(cancellationToken);
        if (jobRun.Status != "Cancelled")
        {
            jobRun.Status = result.Success ? "Completed" : "Failed";
            jobRun.ResultJson = JsonSerializer.Serialize(result.Summary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            jobRun.ErrorMessage = result.ErrorMessage;
            jobRun.ProgressPercent = result.Success ? 100 : jobRun.ProgressPercent;
        }
        jobRun.CompletedAtUtc ??= DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await queue.AcknowledgeAsync(item.StreamId, cancellationToken);
        return true;
    }
}
