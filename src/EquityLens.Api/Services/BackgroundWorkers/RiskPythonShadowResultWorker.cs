using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.BackgroundWorkers;

public sealed class RiskPythonShadowResultWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RiskPythonShadowResultWorker> _logger;
    private readonly RiskPythonOptions _options;
    private readonly string _consumerName =
        $"risk-shadow-result-{Environment.MachineName}-{Guid.NewGuid():N}";

    public RiskPythonShadowResultWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RiskPythonShadowResultWorker> logger,
        IOptions<RiskPythonOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ShadowEnabled)
        {
            _logger.LogInformation("Python risk shadow result worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(_options.ResultPollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Python risk shadow result worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IRiskPythonShadowQueue>();
        var comparisonService = scope.ServiceProvider.GetRequiredService<IRiskShadowComparisonService>();
        var item = await queue.ReadResultAsync(_consumerName, cancellationToken);
        if (item is null) return false;

        string? resultJson = null;
        if (!string.IsNullOrWhiteSpace(item.ResultKey))
            resultJson = await queue.GetResultJsonAsync(item.ResultKey, cancellationToken);
        await comparisonService.RecordCandidateAsync(item, resultJson, cancellationToken);
        await queue.AcknowledgeResultAsync(item.StreamId, cancellationToken);
        return true;
    }
}
