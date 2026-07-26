using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.RiskAnalysis;

public interface IRiskCalculationRunService
{
    Task<Result<RiskCalculationRunResponse>> CreateAsync(
        Guid portfolioId, CreateRiskCalculationRequest request, Guid userId,
        CancellationToken cancellationToken = default);
    Task<Result<RiskCalculationRunResponse>> GetAsync(
        Guid portfolioId, Guid runId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskCalculationRunResponse>> ListAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default);
    Task CompletePythonResultAsync(
        RiskPythonShadowResultItem item, string? resultJson,
        CancellationToken cancellationToken = default);
}

public sealed class RiskCalculationRunService : IRiskCalculationRunService
{
    private const string VtGarchModel = "VT-GARCH-t + Joint-Vector FHS";
    private const string FallbackModel = "C# MVEWMA-FHS";
    private static readonly HashSet<string> Operations = new(StringComparer.OrdinalIgnoreCase)
    {
        "risk", "monte-carlo", "backtest", "scenario", "governance", "report"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EquityLensDbContext _dbContext;
    private readonly IRiskBacktestInputProvider _inputProvider;
    private readonly IRiskPythonShadowQueue _queue;
    private readonly IRiskBacktestEngine _fallbackEngine;
    private readonly IRiskAnalysisService _riskAnalysisService;
    private readonly RiskPythonOptions _options;

    public RiskCalculationRunService(
        EquityLensDbContext dbContext,
        IRiskBacktestInputProvider inputProvider,
        IRiskPythonShadowQueue queue,
        IRiskBacktestEngine fallbackEngine,
        IRiskAnalysisService riskAnalysisService,
        IOptions<RiskPythonOptions> options)
    {
        _dbContext = dbContext;
        _inputProvider = inputProvider;
        _queue = queue;
        _fallbackEngine = fallbackEngine;
        _riskAnalysisService = riskAnalysisService;
        _options = options.Value;
    }

    public async Task<Result<RiskCalculationRunResponse>> CreateAsync(
        Guid portfolioId,
        CreateRiskCalculationRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var operation = request.Operation.Trim().ToLowerInvariant();
        if (!Operations.Contains(operation))
            return Result<RiskCalculationRunResponse>.Failure(
                "risk.invalid_operation", "Unsupported risk calculation operation.");
        if (request.Simulations is <= 0 or > 100_000)
            return Result<RiskCalculationRunResponse>.Failure(
                "risk.invalid_simulations", "Simulations must be between 1 and 100000.");
        var owned = await _dbContext.Portfolios.AnyAsync(
            x => x.Id == portfolioId && x.OwnerUserId == userId, cancellationToken);
        if (!owned)
            return Result<RiskCalculationRunResponse>.Failure(
                "portfolio.not_found", "Portfolio was not found.");

        var to = request.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = request.From ?? to.AddYears(-3);
        var prepared = await _inputProvider.PreparePortfolioRiskBacktestInputAsync(
            portfolioId, from, to, userId, cancellationToken);
        if (!prepared.IsSuccess)
            return Result<RiskCalculationRunResponse>.Failure(
                prepared.ErrorCode!, prepared.ErrorMessage!);
        var input = prepared.Value! with { Simulations = request.Simulations };
        var run = new RiskCalculationRun
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            RequestedByUserId = userId,
            Operation = operation,
            Status = "Queued",
            RequestedModel = VtGarchModel,
            AlgorithmVersion = _options.CandidateAlgorithmVersion,
            DataFactorVersion = _options.DataFactorVersion,
            InputSnapshotJson = JsonSerializer.Serialize(new
            {
                from, to, request.Simulations, operation, request.Parameters
            }, JsonOptions),
            CreatedAtUtc = DateTime.UtcNow,
        };
        _dbContext.RiskCalculationRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!_options.PrimaryEnabled)
        {
            await CompleteFallbackAsync(run, input, "python_primary_disabled", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<RiskCalculationRunResponse>.Success(ToResponse(run));
        }

        try
        {
            var queued = await _queue.EnqueueCalculationAsync(
                run.Id, operation, input, cancellationToken);
            run.InputHash = queued.InputHash;
            run.Status = "Running";
            run.ProgressPercent = 30;
            run.StartedAtUtc = DateTime.UtcNow;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await CompleteFallbackAsync(run, input, "python_enqueue_failed", cancellationToken);
            run.ErrorMessage = $"Python enqueue failed; C# fallback completed: {exception.Message}";
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RiskCalculationRunResponse>.Success(ToResponse(run));
    }

    public async Task<Result<RiskCalculationRunResponse>> GetAsync(
        Guid portfolioId, Guid runId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.RiskCalculationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId && x.PortfolioId == portfolioId &&
                x.RequestedByUserId == userId, cancellationToken);
        return run is null
            ? Result<RiskCalculationRunResponse>.Failure(
                "risk.calculation_not_found", "Risk calculation was not found.")
            : Result<RiskCalculationRunResponse>.Success(ToResponse(run));
    }

    public async Task<IReadOnlyList<RiskCalculationRunResponse>> ListAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default) =>
        (await _dbContext.RiskCalculationRuns.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.RequestedByUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50)
            .ToListAsync(cancellationToken)).Select(ToResponse).ToArray();

    public async Task CompletePythonResultAsync(
        RiskPythonShadowResultItem item,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        if (item.CalculationRunId is not Guid runId) return;
        var run = await _dbContext.RiskCalculationRuns
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null || run.Status is "Completed" or "Failed") return;
        if (string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(resultJson))
        {
            try
            {
                using var document = JsonDocument.Parse(resultJson);
                var root = document.RootElement;
                if (root.GetProperty("outcome").GetString() == "Success" &&
                    root.TryGetProperty("value", out var value))
                {
                    run.ResultJson = value.GetRawText();
                    run.SelectedModel = VtGarchModel;
                    run.Status = "Completed";
                    run.ProgressPercent = 100;
                    run.CompletedAtUtc = DateTime.UtcNow;
                    if (value.TryGetProperty("fitHealth", out var health))
                        run.FitHealthJson = health.GetRawText();
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return;
                }
            }
            catch (JsonException)
            {
                // Invalid output follows the explicit fallback path.
            }
        }

        var snapshot = JsonSerializer.Deserialize<CalculationSnapshot>(
            run.InputSnapshotJson, JsonOptions);
        var from = snapshot?.From ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3);
        var to = snapshot?.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var prepared = await _inputProvider.PreparePortfolioRiskBacktestInputAsync(
            run.PortfolioId, from, to, run.RequestedByUserId, cancellationToken);
        if (!prepared.IsSuccess)
        {
            run.Status = "Failed";
            run.ErrorCode = prepared.ErrorCode;
            run.ErrorMessage = prepared.ErrorMessage;
            run.CompletedAtUtc = DateTime.UtcNow;
        }
        else
        {
            await CompleteFallbackAsync(
                run, prepared.Value!, "python_worker_failed", cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteFallbackAsync(
        RiskCalculationRun run,
        RiskBacktestEngineInput input,
        string reason,
        CancellationToken cancellationToken)
    {
        run.Status = "FallbackRunning";
        run.ProgressPercent = 70;
        run.FallbackDepth = 1;
        run.FallbackReason = reason;
        object? value = null;
        string? errorCode = null;
        string? errorMessage = null;
        switch (run.Operation)
        {
            case "risk":
                {
                    var result = await _riskAnalysisService.GetPortfolioRiskAsync(
                        run.PortfolioId, input.From, input.To, 30, .95m,
                        input.Simulations, run.RequestedByUserId, cancellationToken,
                        "mvewma_fhs");
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
            case "monte-carlo":
                {
                    var result = await _riskAnalysisService.GetPortfolioMonteCarloAsync(
                        run.PortfolioId, run.RequestedByUserId, cancellationToken,
                        "mvewma_fhs");
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
            case "governance":
                {
                    var result = await _riskAnalysisService.GetPortfolioRiskGovernanceAsync(
                        run.PortfolioId, run.RequestedByUserId, cancellationToken);
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
            case "report":
                {
                    var result = await _riskAnalysisService.CreatePortfolioRiskReportSnapshotAsync(
                        run.PortfolioId, run.RequestedByUserId, cancellationToken);
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
            case "scenario":
                {
                    var snapshot = JsonSerializer.Deserialize<CalculationSnapshot>(
                        run.InputSnapshotJson, JsonOptions);
                    var scenarioRequest = snapshot?.Parameters is JsonElement parameters &&
                        parameters.ValueKind == JsonValueKind.Object &&
                        parameters.TryGetProperty("targetWeights", out var targetWeights)
                        ? new PortfolioRiskScenarioRequest(
                            targetWeights.Deserialize<PortfolioRiskScenarioWeightRequest[]>(
                                JsonOptions) ?? [])
                        : new PortfolioRiskScenarioRequest([]);
                    var result = await _riskAnalysisService.CalculatePortfolioRiskScenarioAsync(
                        run.PortfolioId, scenarioRequest, run.RequestedByUserId,
                        cancellationToken);
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
            default:
                {
                    var result = _fallbackEngine.Calculate(input, cancellationToken);
                    value = result.Value;
                    errorCode = result.ErrorCode;
                    errorMessage = result.ErrorMessage;
                    break;
                }
        }
        if (value is null)
        {
            run.Status = "Failed";
            run.ErrorCode = errorCode;
            run.ErrorMessage = errorMessage;
        }
        else
        {
            run.ResultJson = JsonSerializer.Serialize(value, JsonOptions);
            run.SelectedModel = FallbackModel;
            run.AlgorithmVersion = _fallbackEngine.AlgorithmVersion;
            run.Status = "Completed";
            run.ProgressPercent = 100;
        }
        run.CompletedAtUtc = DateTime.UtcNow;
    }

    private static RiskCalculationRunResponse ToResponse(RiskCalculationRun run)
    {
        JsonElement? result = null;
        if (!string.IsNullOrWhiteSpace(run.ResultJson))
        {
            using var document = JsonDocument.Parse(run.ResultJson);
            result = document.RootElement.Clone();
        }
        return new(
            run.Id, run.PortfolioId, run.Operation, run.Status, run.ProgressPercent,
            run.RequestedModel, run.SelectedModel, run.AlgorithmVersion, run.InputHash,
            run.DataFactorVersion, run.FallbackReason, run.FallbackDepth,
            run.CreatedAtUtc, run.StartedAtUtc, run.CompletedAtUtc,
            run.ErrorCode, run.ErrorMessage, result);
    }

    private sealed record CalculationSnapshot(
        DateOnly From, DateOnly To, JsonElement? Parameters = null);
}
