using System.Diagnostics;
using EquityLens.Api.Common;
using EquityLens.Api.Observability;
using Microsoft.Extensions.Logging;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class RiskOperationTelemetry(ILogger<RiskAnalysisService>? logger = null)
{
    public async Task<Result<T>> ExecuteAsync<T>(
        string operation,
        string? model,
        decimal? confidenceLevel,
        int? simulations,
        Func<Task<Result<T>>> execute)
    {
        using var activity = EquityLensTelemetry.StartRiskOperation(operation, model, confidenceLevel, simulations);
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Risk operation {RiskOperation} started. Model={RiskModel} Confidence={RiskConfidence} Simulations={RiskSimulations}",
            operation, model, confidenceLevel, simulations);

        try
        {
            var result = await execute();
            stopwatch.Stop();
            EquityLensTelemetry.CompleteRiskOperation(activity, operation, stopwatch, result.IsSuccess, result.ErrorCode, model, confidenceLevel);
            logger?.LogInformation("Risk operation {RiskOperation} completed. Outcome={RiskOutcome} DurationMs={RiskDurationMs} ErrorCode={RiskErrorCode}",
                operation, result.IsSuccess ? "success" : "failure", stopwatch.ElapsedMilliseconds, result.ErrorCode);
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            EquityLensTelemetry.MarkError(activity, exception);
            EquityLensTelemetry.CompleteRiskOperation(activity, operation, stopwatch, false, exception.GetType().Name, model, confidenceLevel);
            logger?.LogError(exception, "Risk operation {RiskOperation} failed after {RiskDurationMs}ms", operation, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
