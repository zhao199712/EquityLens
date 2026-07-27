using System.Text.Json;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed record RiskShadowDifferenceSummary(
    int ComparedPointCount,
    int OutsideToleranceCount,
    decimal OutsideToleranceRate,
    decimal MaxAbsoluteVarDifference,
    decimal MaxAbsoluteEsDifference,
    decimal MeanAbsoluteVarDifference,
    decimal MeanAbsoluteEsDifference,
    decimal AbsoluteTolerance,
    decimal RelativeTolerance);

public interface IRiskShadowComparisonService
{
    Task RecordCandidateAsync(
        RiskPythonShadowResultItem item,
        string? resultJson,
        CancellationToken cancellationToken = default);
    Task TryFinalizeAsync(Guid comparisonId, CancellationToken cancellationToken = default);
}

public sealed class RiskShadowComparisonService : IRiskShadowComparisonService
{
    private const decimal AbsoluteTolerance = 0.005m;
    private const decimal RelativeTolerance = 0.05m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EquityLensDbContext _dbContext;

    public RiskShadowComparisonService(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RecordCandidateAsync(
        RiskPythonShadowResultItem item,
        string? resultJson,
        CancellationToken cancellationToken = default)
    {
        var comparison = await _dbContext.RiskEngineComparisons
            .FirstOrDefaultAsync(x => x.Id == item.ComparisonId, cancellationToken);
        if (comparison is null) return;

        comparison.CandidateDurationMs = item.DurationMs;
        comparison.CandidateResultJson = resultJson;
        comparison.ErrorMessage = item.ErrorMessage;
        comparison.Status = string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(resultJson)
            ? "CandidateCompleted"
            : "CandidateFailed";
        if (comparison.Status == "CandidateFailed" && string.IsNullOrWhiteSpace(comparison.ErrorMessage))
            comparison.ErrorMessage = "Candidate result payload is missing or expired.";
        if (comparison.Status == "CandidateFailed")
            comparison.CompletedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (comparison.Status == "CandidateCompleted")
            await TryFinalizeAsync(comparison.Id, cancellationToken);
    }

    public async Task TryFinalizeAsync(Guid comparisonId, CancellationToken cancellationToken = default)
    {
        var comparison = await _dbContext.RiskEngineComparisons
            .Include(x => x.RiskBacktestRun)
            .FirstOrDefaultAsync(x => x.Id == comparisonId, cancellationToken);
        if (comparison is null || string.IsNullOrWhiteSpace(comparison.CandidateResultJson) ||
            string.IsNullOrWhiteSpace(comparison.RiskBacktestRun.ResultJson))
            return;

        RiskBacktestEngineResult? candidate;
        PortfolioRiskBacktestResponse? primary;
        try
        {
            candidate = JsonSerializer.Deserialize<RiskBacktestEngineResult>(
                comparison.CandidateResultJson, JsonOptions);
            primary = JsonSerializer.Deserialize<PortfolioRiskBacktestResponse>(
                comparison.RiskBacktestRun.ResultJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            comparison.Status = "CandidateFailed";
            comparison.ErrorMessage = $"Candidate result JSON is invalid: {exception.Message}";
            comparison.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (candidate?.Value is null || candidate.Outcome != RiskEngineOutcomes.Success || primary is null)
        {
            comparison.Status = "CandidateFailed";
            comparison.ErrorMessage = candidate?.ErrorMessage ?? "Candidate result did not contain a successful value.";
            comparison.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var summary = Compare(primary, candidate.Value);
        comparison.PassedTolerance = summary.ComparedPointCount > 0 &&
            summary.OutsideToleranceRate <= 0.05m;
        comparison.ComparisonJson = JsonSerializer.Serialize(summary, JsonOptions);
        comparison.Status = comparison.PassedTolerance.Value ? "Passed" : "Diverged";
        comparison.CompletedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static RiskShadowDifferenceSummary Compare(
        PortfolioRiskBacktestResponse primary,
        PortfolioRiskBacktestResponse candidate)
    {
        var candidateModels = candidate.Models.ToDictionary(
            model => (model.Model, model.ConfidenceLevel));
        var varDifferences = new List<decimal>();
        var esDifferences = new List<decimal>();
        var outside = 0;
        foreach (var primaryModel in primary.Models)
        {
            if (!candidateModels.TryGetValue((primaryModel.Model, primaryModel.ConfidenceLevel), out var candidateModel))
            {
                outside += primaryModel.Points.Count;
                continue;
            }

            var candidatePoints = candidateModel.Points.ToDictionary(point => point.Date);
            foreach (var primaryPoint in primaryModel.Points)
            {
                if (!candidatePoints.TryGetValue(primaryPoint.Date, out var candidatePoint))
                {
                    outside++;
                    continue;
                }

                var varDifference = Math.Abs(primaryPoint.PredictedVaR - candidatePoint.PredictedVaR);
                var esDifference = Math.Abs(primaryPoint.PredictedES - candidatePoint.PredictedES);
                varDifferences.Add(varDifference);
                esDifferences.Add(esDifference);
                if (!WithinTolerance(primaryPoint.PredictedVaR, candidatePoint.PredictedVaR) ||
                    !WithinTolerance(primaryPoint.PredictedES, candidatePoint.PredictedES))
                    outside++;
            }
        }

        return new RiskShadowDifferenceSummary(
            varDifferences.Count,
            outside,
            varDifferences.Count == 0 ? 1 : (decimal)outside / varDifferences.Count,
            varDifferences.Count == 0 ? 0 : varDifferences.Max(),
            esDifferences.Count == 0 ? 0 : esDifferences.Max(),
            varDifferences.Count == 0 ? 0 : varDifferences.Average(),
            esDifferences.Count == 0 ? 0 : esDifferences.Average(),
            AbsoluteTolerance,
            RelativeTolerance);
    }

    private static bool WithinTolerance(decimal primary, decimal candidate)
    {
        var difference = Math.Abs(primary - candidate);
        if (difference <= AbsoluteTolerance) return true;
        var denominator = Math.Max(Math.Abs(primary), 0.000001m);
        return difference / denominator <= RelativeTolerance;
    }
}
