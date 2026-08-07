using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class RiskBacktestRunService : IRiskBacktestRunService
{
    private const int LookbackDays = 252;
    private const int Simulations = 5000;
    private const string RequestedModel = "VT-GARCH-t + Joint-Vector FHS";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EquityLensDbContext _dbContext;
    private readonly IBackgroundJobQueue _queue;
    private readonly IRiskBacktestInputProvider _inputProvider;
    private readonly IRiskBacktestEngine _riskBacktestEngine;
    private readonly IRiskPythonShadowQueue _shadowQueue;
    private readonly IRiskShadowComparisonService _comparisonService;
    private readonly RiskPythonOptions _pythonOptions;
    private readonly ILogger<RiskBacktestRunService> _logger;
    private readonly IRiskQualityValidationService _qualityValidation;

    public RiskBacktestRunService(
        EquityLensDbContext dbContext,
        IBackgroundJobQueue queue,
        IRiskBacktestInputProvider inputProvider,
        IRiskBacktestEngine riskBacktestEngine,
        IRiskPythonShadowQueue shadowQueue,
        IRiskShadowComparisonService comparisonService,
        IOptions<RiskPythonOptions> pythonOptions,
        ILogger<RiskBacktestRunService> logger,
        IRiskQualityValidationService qualityValidation)
    {
        _dbContext = dbContext;
        _queue = queue;
        _inputProvider = inputProvider;
        _riskBacktestEngine = riskBacktestEngine;
        _shadowQueue = shadowQueue;
        _comparisonService = comparisonService;
        _pythonOptions = pythonOptions.Value;
        _logger = logger;
        _qualityValidation = qualityValidation;
    }

    public async Task<Result<PortfolioRiskBacktestRunResponse>> CreateAsync(Guid portfolioId, DateOnly from, DateOnly to, Guid userId, CancellationToken cancellationToken = default)
    {
        if (from >= to)
            return Result<PortfolioRiskBacktestRunResponse>.Failure("risk.invalid_date_range", "'from' must be earlier than 'to'.");

        var owned = await _dbContext.Portfolios.AnyAsync(x => x.Id == portfolioId && x.OwnerUserId == userId, cancellationToken);
        if (!owned)
            return Result<PortfolioRiskBacktestRunResponse>.Failure("portfolio.not_found", "Portfolio was not found.");

        var now = DateTime.UtcNow;
        var runId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var algorithmVersion = _pythonOptions.PrimaryEnabled
            ? _pythonOptions.CandidateAlgorithmVersion
            : CSharpRiskBacktestEngine.CurrentAlgorithmVersion;
        var inputSnapshot = JsonSerializer.Serialize(new { from, to, lookbackDays = LookbackDays, simulations = Simulations, algorithmVersion }, JsonOptions);
        var payload = JsonSerializer.Serialize(new { backtestRunId = runId }, JsonOptions);
        var job = new JobRun
        {
            Id = jobId,
            CreatedByUserId = userId,
            JobType = "PortfolioRiskBacktest",
            Status = "Queued",
            PayloadJson = payload,
            CreatedAtUtc = now,
        };
        var run = new RiskBacktestRun
        {
            Id = runId,
            PortfolioId = portfolioId,
            RequestedByUserId = userId,
            JobRunId = jobId,
            FromDate = from,
            ToDate = to,
            LookbackDays = LookbackDays,
            Simulations = Simulations,
            AlgorithmVersion = algorithmVersion,
            RequestedModel = _pythonOptions.PrimaryEnabled ? RequestedModel : "C# MVEWMA-FHS",
            Status = "Queued",
            InputSnapshotJson = inputSnapshot,
            CreatedAtUtc = now,
        };
        _dbContext.JobRuns.Add(job);
        _dbContext.RiskBacktestRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var streamId = await _queue.EnqueueAsync(new BackgroundJob(jobId, job.JobType, runId.ToString(), payload, now), cancellationToken);
        job.RedisJobId = streamId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<PortfolioRiskBacktestRunResponse>.Success(ToResponse(run));
    }

    public async Task<Result<PortfolioRiskBacktestRunResponse>> GetAsync(Guid portfolioId, Guid runId, Guid userId, CancellationToken cancellationToken = default)
    {
        var run = await FindOwnedAsync(portfolioId, runId, userId, cancellationToken);
        return run is null
            ? Result<PortfolioRiskBacktestRunResponse>.Failure("risk.backtest_run_not_found", "Backtest run was not found.")
            : Result<PortfolioRiskBacktestRunResponse>.Success(ToResponse(run));
    }

    public async Task<IReadOnlyList<PortfolioRiskBacktestRunResponse>> ListAsync(Guid portfolioId, Guid userId, CancellationToken cancellationToken = default)
    {
        var runs = await _dbContext.RiskBacktestRuns.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.RequestedByUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return runs.Select(ToResponse).ToArray();
    }

    public async Task<bool> ExecuteAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.RiskBacktestRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return false;
        if (run.Status == "Completed") return true;
        if (run.Status == "Failed") return false;

        run.Status = "Running";
        run.ProgressPercent = 10;
        run.StartedAtUtc ??= DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Portfolio risk backtest run {BacktestRunId} started.", run.Id);

        try
        {
            var prepared = await _inputProvider.PreparePortfolioRiskBacktestInputAsync(
                run.PortfolioId, run.FromDate, run.ToDate, run.RequestedByUserId, cancellationToken);
            if (!prepared.IsSuccess)
            {
                run.Status = "Failed";
                run.ErrorCode = prepared.ErrorCode;
                run.ErrorMessage = prepared.ErrorMessage;
                return false;
            }

            var input = prepared.Value! with { Simulations = run.Simulations };
            RiskEngineComparison? comparison = null;
            if (_pythonOptions.ShadowEnabled || _pythonOptions.PrimaryEnabled)
            {
                comparison = new RiskEngineComparison
                {
                    Id = Guid.NewGuid(),
                    RiskBacktestRunId = run.Id,
                    PrimaryEngine = _riskBacktestEngine.EngineName,
                    PrimaryAlgorithmVersion = _riskBacktestEngine.AlgorithmVersion,
                    CandidateEngine = "python",
                    CandidateAlgorithmVersion = _pythonOptions.CandidateAlgorithmVersion,
                    InputHash = "pending",
                    Status = "Queueing",
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _dbContext.RiskEngineComparisons.Add(comparison);
                await _dbContext.SaveChangesAsync(cancellationToken);
                try
                {
                    var enqueued = await _shadowQueue.EnqueueAsync(
                        comparison.Id, run.Id, input, cancellationToken);
                    comparison.InputHash = enqueued.Job.InputHash;
                    comparison.Status = "Queued";
                    run.InputHash = enqueued.Job.InputHash;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (_pythonOptions.PrimaryEnabled)
                    {
                        run.ProgressPercent = 40;
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        return true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    comparison.Status = "CandidateFailed";
                    comparison.ErrorMessage = $"Could not enqueue Python shadow calculation: {exception.Message}";
                    comparison.CompletedAtUtc = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    run.FallbackReason = "python_enqueue_failed";
                    run.FallbackDepth = 1;
                    _logger.LogWarning(exception,
                        "Python enqueue failed for backtest run {BacktestRunId}; C# fallback will continue.",
                        run.Id);
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var engineResult = _riskBacktestEngine.Calculate(input, cancellationToken);
            stopwatch.Stop();
            if (comparison is not null)
                comparison.PrimaryDurationMs = stopwatch.ElapsedMilliseconds;
            var result = engineResult.ToResult();
            if (result.IsSuccess)
            {
                run.ResultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
                run.Status = "Completed";
                run.ProgressPercent = 100;
                run.ErrorCode = null;
                run.ErrorMessage = null;
                run.SelectedModel = "C# MVEWMA-FHS";
            }
            else
            {
                run.Status = "Failed";
                run.ErrorCode = result.ErrorCode;
                run.ErrorMessage = result.ErrorMessage;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (comparison is not null)
            {
                await _dbContext.Entry(comparison).ReloadAsync(cancellationToken);
                await _comparisonService.TryFinalizeAsync(comparison.Id, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            run.Status = "Failed";
            run.ErrorCode = exception is OperationCanceledException ? "risk.backtest_cancelled" : "risk.backtest_execution_failed";
            run.ErrorMessage = exception.Message;
            _logger.LogError(exception, "Portfolio risk backtest run {BacktestRunId} failed.", run.Id);
        }
        finally
        {
            var terminal = run.Status is "Completed" or "Failed";
            if (terminal)
                run.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            if (terminal)
                await _qualityValidation.CompleteForBacktestAsync(run.Id, CancellationToken.None);
        }
        return run.Status is "Completed" or "Running";
    }

    public async Task CompletePythonResultAsync(
        RiskPythonShadowResultItem item,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        var comparison = await _dbContext.RiskEngineComparisons
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == item.ComparisonId, cancellationToken);
        if (comparison is null) return;
        var run = await _dbContext.RiskBacktestRuns
            .FirstOrDefaultAsync(x => x.Id == comparison.RiskBacktestRunId, cancellationToken);
        if (run is null || run.Status is "Completed" or "Failed") return;

        if (string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(resultJson))
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<RiskBacktestEngineResult>(resultJson, JsonOptions);
                if (candidate?.Outcome == RiskEngineOutcomes.Success && candidate.Value is not null)
                {
                    run.ResultJson = JsonSerializer.Serialize(candidate.Value, JsonOptions);
                    run.SelectedModel = RequestedModel;
                    run.AlgorithmVersion = _pythonOptions.CandidateAlgorithmVersion;
                    run.Status = "Completed";
                    run.ProgressPercent = 100;
                    run.ErrorCode = null;
                    run.ErrorMessage = null;
                    run.CompletedAtUtc = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await _qualityValidation.CompleteForBacktestAsync(run.Id, cancellationToken);
                    return;
                }
            }
            catch (JsonException)
            {
                // Invalid candidate output follows the explicit fallback path below.
            }
        }

        run.Status = "FallbackRunning";
        run.ProgressPercent = 70;
        run.FallbackDepth = 1;
        run.FallbackReason = string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? "python_candidate_invalid"
            : "python_worker_failed";
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _qualityValidation.CompleteForBacktestAsync(run.Id, cancellationToken);

        var prepared = await _inputProvider.PreparePortfolioRiskBacktestInputAsync(
            run.PortfolioId, run.FromDate, run.ToDate, run.RequestedByUserId, cancellationToken);
        if (!prepared.IsSuccess)
        {
            run.Status = "Failed";
            run.ErrorCode = prepared.ErrorCode;
            run.ErrorMessage = prepared.ErrorMessage;
        }
        else
        {
            var fallback = _riskBacktestEngine.Calculate(prepared.Value!, cancellationToken);
            if (fallback.Value is not null)
            {
                run.ResultJson = JsonSerializer.Serialize(fallback.Value, JsonOptions);
                run.SelectedModel = "C# MVEWMA-FHS";
                run.AlgorithmVersion = _riskBacktestEngine.AlgorithmVersion;
                run.Status = "Completed";
                run.ProgressPercent = 100;
            }
            else
            {
                run.Status = "Failed";
                run.ErrorCode = fallback.ErrorCode;
                run.ErrorMessage = fallback.ErrorMessage;
            }
        }
        run.CompletedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<RiskBacktestRun?> FindOwnedAsync(Guid portfolioId, Guid runId, Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.RiskBacktestRuns.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == runId && x.PortfolioId == portfolioId && x.RequestedByUserId == userId, cancellationToken);

    private static PortfolioRiskBacktestRunResponse ToResponse(RiskBacktestRun run) => new(
        run.Id, run.PortfolioId, run.JobRunId, run.Status, run.ProgressPercent,
        run.FromDate, run.ToDate, run.LookbackDays, run.Simulations, run.AlgorithmVersion,
        run.RequestedModel, run.SelectedModel, run.InputHash, run.FallbackReason, run.FallbackDepth,
        run.CreatedAtUtc, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorCode, run.ErrorMessage,
        string.IsNullOrWhiteSpace(run.ResultJson) ? null : JsonSerializer.Deserialize<PortfolioRiskBacktestResponse>(run.ResultJson, JsonOptions));
}
