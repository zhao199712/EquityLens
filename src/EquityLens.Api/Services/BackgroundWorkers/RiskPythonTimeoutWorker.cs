using EquityLens.Api.Data;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.BackgroundWorkers;

/// <summary>將逾時的 Python primary 工作送入明確的 C# fallback。</summary>
public sealed class RiskPythonTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RiskPythonOptions _options;

    public RiskPythonTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<RiskPythonOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PrimaryEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
            var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.CalculationTimeoutMinutes));
            var calculations = await db.RiskCalculationRuns.AsNoTracking()
                .Where(x => x.Status == "Running" && x.StartedAtUtc < cutoff)
                .Select(x => x.Id).Take(20).ToListAsync(stoppingToken);
            var calculationService =
                scope.ServiceProvider.GetRequiredService<IRiskCalculationRunService>();
            foreach (var id in calculations)
                await calculationService.CompletePythonResultAsync(
                    new RiskPythonShadowResultItem(
                        "timeout", Guid.Empty, Guid.Empty, id, "Failed", null, null,
                        "python_worker_timeout"),
                    null,
                    stoppingToken);

            var comparisons = await db.RiskEngineComparisons.AsNoTracking()
                .Where(x => x.Status == "Queued" && x.CreatedAtUtc < cutoff)
                .Select(x => new { x.Id, x.RiskBacktestRunId }).Take(20)
                .ToListAsync(stoppingToken);
            var backtestService =
                scope.ServiceProvider.GetRequiredService<IRiskBacktestRunService>();
            foreach (var comparison in comparisons)
                await backtestService.CompletePythonResultAsync(
                    new RiskPythonShadowResultItem(
                        "timeout", Guid.Empty, comparison.Id, null, "Failed", null, null,
                        "python_worker_timeout"),
                    null,
                    stoppingToken);
        }
    }
}
