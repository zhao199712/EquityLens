using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Redis;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class RiskBacktestRunService : IRiskBacktestRunService
{
    private const int LookbackDays = 252;
    private const int Simulations = 5000;
    private const string AlgorithmVersion = "mvewma-fhs-backtest-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EquityLensDbContext _dbContext;
    private readonly IBackgroundJobQueue _queue;
    private readonly IRiskAnalysisService _riskAnalysisService;
    private readonly ILogger<RiskBacktestRunService> _logger;

    public RiskBacktestRunService(EquityLensDbContext dbContext, IBackgroundJobQueue queue,
        IRiskAnalysisService riskAnalysisService, ILogger<RiskBacktestRunService> logger)
    {
        _dbContext = dbContext;
        _queue = queue;
        _riskAnalysisService = riskAnalysisService;
        _logger = logger;
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
        var inputSnapshot = JsonSerializer.Serialize(new { from, to, lookbackDays = LookbackDays, simulations = Simulations, algorithmVersion = AlgorithmVersion }, JsonOptions);
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
            AlgorithmVersion = AlgorithmVersion,
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
            var result = await _riskAnalysisService.GetPortfolioRiskBacktestAsync(run.PortfolioId, run.FromDate, run.ToDate, run.RequestedByUserId, cancellationToken);
            if (result.IsSuccess)
            {
                run.ResultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
                run.Status = "Completed";
                run.ProgressPercent = 100;
                run.ErrorCode = null;
                run.ErrorMessage = null;
            }
            else
            {
                run.Status = "Failed";
                run.ErrorCode = result.ErrorCode;
                run.ErrorMessage = result.ErrorMessage;
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
            run.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        return run.Status == "Completed";
    }

    private async Task<RiskBacktestRun?> FindOwnedAsync(Guid portfolioId, Guid runId, Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.RiskBacktestRuns.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == runId && x.PortfolioId == portfolioId && x.RequestedByUserId == userId, cancellationToken);

    private static PortfolioRiskBacktestRunResponse ToResponse(RiskBacktestRun run) => new(
        run.Id, run.PortfolioId, run.JobRunId, run.Status, run.ProgressPercent,
        run.FromDate, run.ToDate, run.LookbackDays, run.Simulations, run.AlgorithmVersion,
        run.CreatedAtUtc, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorCode, run.ErrorMessage,
        string.IsNullOrWhiteSpace(run.ResultJson) ? null : JsonSerializer.Deserialize<PortfolioRiskBacktestResponse>(run.ResultJson, JsonOptions));
}
