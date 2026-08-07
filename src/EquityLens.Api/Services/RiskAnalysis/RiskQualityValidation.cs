using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.RiskAnalysis;

public static class RiskQualityPolicy
{
    public const string Version = "risk-quality-v1";
    public const decimal ConfidenceLevel = .95m;
    public const decimal PValueThreshold = .05m;
    public const int MinimumObservations = 100;
}

public interface IRiskQualityValidationService
{
    Task<Result<RiskQualityEvaluationResponse>> EnsureAsync(Guid portfolioId, Guid calculationRunId,
        Guid userId, CancellationToken cancellationToken = default);
    Task CompleteForBacktestAsync(Guid backtestRunId, CancellationToken cancellationToken = default);
}

public sealed class RiskQualityValidationService(
    EquityLensDbContext db,
    IBackgroundJobQueue queue,
    IOptions<RiskPythonOptions> pythonOptions) : IRiskQualityValidationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<RiskQualityEvaluationResponse>> EnsureAsync(Guid portfolioId, Guid calculationRunId,
        Guid userId, CancellationToken cancellationToken = default)
    {
        var calculation = await db.RiskCalculationRuns.Include(x => x.QualityEvaluations)
            .FirstOrDefaultAsync(x => x.Id == calculationRunId && x.PortfolioId == portfolioId &&
                x.RequestedByUserId == userId, cancellationToken);
        if (calculation is null)
            return Result<RiskQualityEvaluationResponse>.Failure("risk.calculation_not_found", "Risk calculation was not found.");
        if (calculation.Status != "Completed" || calculation.Operation != "risk")
            return Result<RiskQualityEvaluationResponse>.Failure("risk.quality_not_ready", "Only completed risk calculations can be validated.");

        var existing = calculation.QualityEvaluations.SingleOrDefault(x => x.PolicyVersion == RiskQualityPolicy.Version);
        if (existing is not null && existing.Status != "Error")
            return Result<RiskQualityEvaluationResponse>.Success(ToResponse(existing));
        if (!TrySnapshot(calculation.InputSnapshotJson, out var snapshot))
            return Result<RiskQualityEvaluationResponse>.Failure("risk.input_snapshot_invalid", "Risk calculation input snapshot is invalid.");

        var model = BacktestModel(calculation.SelectedModel);
        var reusable = string.IsNullOrWhiteSpace(calculation.InputHash) ? null : await db.RiskBacktestRuns.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.RequestedByUserId == userId &&
                x.FromDate == snapshot.From && x.ToDate == snapshot.To && x.Simulations == snapshot.Simulations &&
                x.Status == "Completed" && x.InputHash == calculation.InputHash && x.ResultJson != null)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(cancellationToken);

        if (reusable is not null)
        {
            var evaluation = existing ?? NewEvaluation(calculation, reusable.Id, model);
            if (existing is null) db.RiskQualityEvaluations.Add(evaluation);
            Evaluate(evaluation, calculation, reusable);
            await db.SaveChangesAsync(cancellationToken);
            return Result<RiskQualityEvaluationResponse>.Success(ToResponse(evaluation));
        }

        var now = DateTime.UtcNow;
        var backtest = NewBacktest(calculation, snapshot, now, pythonOptions.Value);
        var job = new JobRun
        {
            Id = backtest.JobRunId, CreatedByUserId = userId, JobType = "PortfolioRiskBacktest",
            Status = "Queued", PayloadJson = JsonSerializer.Serialize(new { backtestRunId = backtest.Id }, Json), CreatedAtUtc = now
        };
        var pending = existing ?? NewEvaluation(calculation, backtest.Id, model);
        pending.RiskBacktestRunId = backtest.Id;
        pending.Status = "Pending";
        pending.FailureCodesJson = "[]";
        pending.WarningCodesJson = "[]";
        pending.EvaluatedAtUtc = null;
        db.JobRuns.Add(job);
        db.RiskBacktestRuns.Add(backtest);
        if (existing is null) db.RiskQualityEvaluations.Add(pending);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            job.RedisJobId = await queue.EnqueueAsync(new BackgroundJob(job.Id, job.JobType,
                backtest.Id.ToString(), job.PayloadJson, now), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            backtest.Status = "Failed";
            backtest.ErrorCode = "risk.quality_queue_failed";
            backtest.ErrorMessage = exception.Message;
            pending.Status = "Error";
            pending.FailureCodesJson = SerializeCodes(["BACKTEST_QUEUE_FAILED"]);
            pending.EvaluatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result<RiskQualityEvaluationResponse>.Success(ToResponse(pending));
    }

    public async Task CompleteForBacktestAsync(Guid backtestRunId, CancellationToken cancellationToken = default)
    {
        var backtest = await db.RiskBacktestRuns.FirstOrDefaultAsync(x => x.Id == backtestRunId, cancellationToken);
        var evaluations = await db.RiskQualityEvaluations
            .Include(x => x.RiskCalculationRun)
            .Where(x => x.RiskBacktestRunId == backtestRunId && x.Status == "Pending")
            .ToListAsync(cancellationToken);
        if (backtest is null || evaluations.Count == 0) return;
        foreach (var evaluation in evaluations)
            Evaluate(evaluation, evaluation.RiskCalculationRun, backtest);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static void Evaluate(RiskQualityEvaluation evaluation, RiskCalculationRun calculation, RiskBacktestRun backtest)
    {
        var failures = new List<string>();
        var warnings = new List<string>();
        evaluation.EvaluatedAtUtc = DateTime.UtcNow;
        if (backtest.Status != "Completed" || string.IsNullOrWhiteSpace(backtest.ResultJson))
        {
            evaluation.Status = "Error";
            evaluation.FailureCodesJson = SerializeCodes(["BACKTEST_FAILED"]);
            return;
        }
        var response = JsonSerializer.Deserialize<PortfolioRiskBacktestResponse>(backtest.ResultJson, Json);
        var model = response?.Models.FirstOrDefault(x => x.Model == evaluation.Model && x.ConfidenceLevel == RiskQualityPolicy.ConfidenceLevel);
        if (model is null)
        {
            evaluation.Status = "Error";
            evaluation.FailureCodesJson = SerializeCodes(["MODEL_RESULT_MISSING"]);
            return;
        }
        evaluation.ObservationCount = model.ObservationCount;
        evaluation.KupiecPValue = model.KupiecPValue;
        evaluation.ChristoffersenPValue = model.ChristoffersenPValue;
        evaluation.EsStatus = model.EsStatus;
        evaluation.FitHealthy = FitHealthy(calculation.FitHealthJson);
        if (model.EsStatus == "underestimated") warnings.Add("ES_UNDERESTIMATED");
        if (model.ObservationCount < RiskQualityPolicy.MinimumObservations ||
            model.KupiecPValue is null || model.ChristoffersenPValue is null)
        {
            evaluation.Status = "InsufficientData";
            failures.Add("INSUFFICIENT_OBSERVATIONS");
        }
        else
        {
            if (model.KupiecPValue < RiskQualityPolicy.PValueThreshold) failures.Add("KUPIEC_FAILED");
            if (model.ChristoffersenPValue < RiskQualityPolicy.PValueThreshold) failures.Add("CHRISTOFFERSEN_FAILED");
            if (evaluation.FitHealthy == false) failures.Add("FIT_UNHEALTHY");
            evaluation.Status = failures.Count == 0 ? "Passed" : "Failed";
        }
        evaluation.FailureCodesJson = SerializeCodes(failures);
        evaluation.WarningCodesJson = SerializeCodes(warnings);
    }

    public static RiskQualityEvaluationResponse ToResponse(RiskQualityEvaluation value) => new(
        value.Id, value.RiskCalculationRunId, value.RiskBacktestRunId, value.PolicyVersion, value.Status,
        value.Model, value.ConfidenceLevel, value.ObservationCount, value.KupiecPValue,
        value.ChristoffersenPValue, value.EsStatus, value.FitHealthy,
        DeserializeCodes(value.FailureCodesJson), DeserializeCodes(value.WarningCodesJson),
        value.CreatedAtUtc, value.EvaluatedAtUtc);

    private static RiskQualityEvaluation NewEvaluation(RiskCalculationRun calculation, Guid backtestId, string model) => new()
    {
        Id = Guid.NewGuid(), RiskCalculationRunId = calculation.Id, RiskBacktestRunId = backtestId,
        PolicyVersion = RiskQualityPolicy.Version, Status = "Pending", Model = model,
        ConfidenceLevel = RiskQualityPolicy.ConfidenceLevel, CreatedAtUtc = DateTime.UtcNow
    };

    private static RiskBacktestRun NewBacktest(RiskCalculationRun calculation, Snapshot snapshot, DateTime now, RiskPythonOptions options) => new()
    {
        Id = Guid.NewGuid(), PortfolioId = calculation.PortfolioId, RequestedByUserId = calculation.RequestedByUserId,
        JobRunId = Guid.NewGuid(), FromDate = snapshot.From, ToDate = snapshot.To, LookbackDays = 252,
        Simulations = snapshot.Simulations, AlgorithmVersion = options.PrimaryEnabled ? options.CandidateAlgorithmVersion : CSharpRiskBacktestEngine.CurrentAlgorithmVersion,
        RequestedModel = calculation.SelectedModel ?? calculation.RequestedModel, Status = "Queued", CreatedAtUtc = now,
        InputSnapshotJson = JsonSerializer.Serialize(new { from = snapshot.From, to = snapshot.To, lookbackDays = 252,
            simulations = snapshot.Simulations, algorithmVersion = calculation.AlgorithmVersion }, Json)
    };

    private static string BacktestModel(string? selectedModel) => selectedModel == "C# MVEWMA-FHS" ? "MVEWMA-FHS" : selectedModel ?? "VT-GARCH-t + Joint-Vector FHS";
    private static bool? FitHealthy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("healthy", out var healthy) ? healthy.GetBoolean() : null;
    }
    private static bool TrySnapshot(string json, out Snapshot snapshot)
    {
        try { snapshot = JsonSerializer.Deserialize<Snapshot>(json, Json)!; return snapshot is not null && snapshot.From != default && snapshot.To != default; }
        catch (JsonException) { snapshot = null!; return false; }
    }
    private static string SerializeCodes(IEnumerable<string> codes) => JsonSerializer.Serialize(codes, Json);
    private static IReadOnlyList<string> DeserializeCodes(string json) => JsonSerializer.Deserialize<string[]>(json, Json) ?? [];
    private sealed record Snapshot(DateOnly From, DateOnly To, int Simulations = 10000);
}
