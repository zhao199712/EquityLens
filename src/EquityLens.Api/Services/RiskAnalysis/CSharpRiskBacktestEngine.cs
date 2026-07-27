using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Domain.Calculations;
using System.Security.Cryptography;
using System.Text;

namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class CSharpRiskBacktestEngine : IRiskBacktestEngine
{
    public const string CurrentAlgorithmVersion = "mvewma-fhs-backtest-v3";
    public string EngineName => "csharp";
    public string AlgorithmVersion => CurrentAlgorithmVersion;

    public RiskBacktestEngineResult Calculate(
        RiskBacktestEngineInput input,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(input);
        if (validation is not null) return validation;

        var windowCount = input.PortfolioReturns.Count - input.LookbackDays;
        var windowResults = new BacktestWindowResult[windowCount];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 4),
        };

        try
        {
            Parallel.For(0, windowCount, parallelOptions, windowOffset =>
            {
                var index = input.LookbackDays + windowOffset;
                var returnMatrix = new IReadOnlyList<decimal>[input.AssetReturns.Count];
                for (var assetIndex = 0; assetIndex < input.AssetReturns.Count; assetIndex++)
                {
                    var source = input.AssetReturns[assetIndex];
                    returnMatrix[assetIndex] = source is decimal[] array
                        ? new ArraySegment<decimal>(array, index - input.LookbackDays, input.LookbackDays)
                        : source.Skip(index - input.LookbackDays).Take(input.LookbackDays).ToArray();
                }

                var portfolioWindow = input.PortfolioReturns is decimal[] portfolioArray
                    ? (IReadOnlyList<decimal>)new ArraySegment<decimal>(
                        portfolioArray, index - input.LookbackDays, input.LookbackDays)
                    : input.PortfolioReturns.Skip(index - input.LookbackDays).Take(input.LookbackDays).ToArray();

                var historicalVaR = new decimal[input.ConfidenceLevels.Count];
                var historicalEs = new decimal[input.ConfidenceLevels.Count];
                for (var confidenceIndex = 0; confidenceIndex < input.ConfidenceLevels.Count; confidenceIndex++)
                {
                    var confidence = input.ConfidenceLevels[confidenceIndex];
                    historicalVaR[confidenceIndex] = RiskMath.CalculateHistoricalVaR(portfolioWindow, confidence);
                    historicalEs[confidenceIndex] = RiskMath.CalculateExpectedShortfall(portfolioWindow, confidence);
                }

                var fhsResults = RiskMath.RunMultivariateFhsSimulationForConfidenceLevels(
                    returnMatrix, input.Weights, 100m, 1, input.Simulations, input.ConfidenceLevels,
                    input.EwmaLambda, input.ShrinkageAlpha, randomSeed: StableWindowSeed(input, windowOffset, false));
                var conservativeResults = RiskMath.RunMultivariateFhsSimulationForConfidenceLevels(
                    returnMatrix, input.Weights, 100m, 1, input.Simulations, input.ConfidenceLevels,
                    input.EwmaLambda, input.ShrinkageAlpha,
                    residualCapQuantile: input.ConservativeResidualCapQuantile,
                    randomSeed: StableWindowSeed(input, windowOffset, true));
                if (fhsResults.Any(result => result.DidFallback) ||
                    conservativeResults.Any(result => result.DidFallback))
                    throw new ArithmeticException(
                        $"MVEWMA-FHS could not produce a stable result for window {windowOffset}.");

                windowResults[windowOffset] = new BacktestWindowResult(
                    input.ReturnDates[index],
                    input.PortfolioReturns[index],
                    historicalVaR,
                    historicalEs,
                    fhsResults.Select(result => result.SimulatedVaR).ToArray(),
                    fhsResults.Select(result => result.SimulatedES).ToArray(),
                    conservativeResults.Select(result => result.SimulatedVaR).ToArray(),
                    conservativeResults.Select(result => result.SimulatedES).ToArray());
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException exception) when (
            exception.Flatten().InnerExceptions.All(inner => inner is ArithmeticException or OverflowException))
        {
            return RiskBacktestEngineResult.Failure(
                RiskEngineOutcomes.NumericalFailure,
                "risk.numerical_failure",
                exception.Flatten().InnerExceptions[0].Message);
        }
        catch (Exception exception) when (exception is ArithmeticException or OverflowException)
        {
            return RiskBacktestEngineResult.Failure(
                RiskEngineOutcomes.NumericalFailure,
                "risk.numerical_failure",
                exception.Message);
        }

        var models = new List<PortfolioRiskBacktestModelResponse>();
        for (var confidenceIndex = 0; confidenceIndex < input.ConfidenceLevels.Count; confidenceIndex++)
        {
            var confidence = input.ConfidenceLevels[confidenceIndex];
            var historical = new List<PortfolioRiskBacktestPoint>();
            var monteCarlo = new List<PortfolioRiskBacktestPoint>();
            var conservativeMonteCarlo = new List<PortfolioRiskBacktestPoint>();
            foreach (var window in windowResults)
            {
                historical.Add(new(window.Date, window.ActualReturn, window.HistoricalVaR[confidenceIndex], window.HistoricalEs[confidenceIndex], window.ActualReturn < window.HistoricalVaR[confidenceIndex]));
                monteCarlo.Add(new(window.Date, window.ActualReturn, window.MonteCarloVaR[confidenceIndex], window.MonteCarloEs[confidenceIndex], window.ActualReturn < window.MonteCarloVaR[confidenceIndex]));
                conservativeMonteCarlo.Add(new(window.Date, window.ActualReturn, window.ConservativeVaR[confidenceIndex], window.ConservativeEs[confidenceIndex], window.ActualReturn < window.ConservativeVaR[confidenceIndex]));
            }

            models.Add(BuildBacktestModel("Historical", confidence, historical));
            models.Add(BuildBacktestModel("MVEWMA-FHS", confidence, monteCarlo));
            models.Add(BuildBacktestModel("MVEWMA-FHS（保守 p99）", confidence, conservativeMonteCarlo));
        }

        return RiskBacktestEngineResult.Success(new PortfolioRiskBacktestResponse(
            input.PortfolioId, input.From, input.To, input.LookbackDays, windowCount, models));
    }

    private static RiskBacktestEngineResult? Validate(RiskBacktestEngineInput input)
    {
        if (input.From >= input.To || input.LookbackDays <= 1 || input.Simulations <= 0 ||
            input.EwmaLambda <= 0 || input.EwmaLambda >= 1 ||
            input.ShrinkageAlpha < 0 || input.ShrinkageAlpha > 1 ||
            input.ConservativeResidualCapQuantile < 0 || input.ConservativeResidualCapQuantile >= 1)
            return Invalid("risk.invalid_engine_parameters", "Risk engine parameters are outside their valid ranges.");

        if (input.ConfidenceLevels.Count == 0 ||
            input.ConfidenceLevels.Any(level => level <= 0 || level >= 1))
            return Invalid("risk.invalid_confidence_level", "Confidence levels must be between zero and one.");

        if (input.AssetReturns.Count == 0 || input.AssetReturns.Count != input.Weights.Count ||
            input.AssetReturns.Any(series => series.Count != input.PortfolioReturns.Count) ||
            input.ReturnDates.Count != input.PortfolioReturns.Count)
            return Invalid("risk.invalid_matrix_dimensions", "Return matrix, dates, and weights must have compatible dimensions.");

        if (input.PortfolioReturns.Count <= input.LookbackDays)
            return RiskBacktestEngineResult.Failure(
                RiskEngineOutcomes.InsufficientData,
                "risk.insufficient_prices",
                "Return history is shorter than the requested lookback window.");

        if (input.Weights.Any(weight => weight < 0) || input.Weights.Sum() <= 0)
            return Invalid("risk.invalid_weights", "Portfolio weights must be non-negative and have a positive sum.");

        return null;
    }

    private static RiskBacktestEngineResult Invalid(string code, string message) =>
        RiskBacktestEngineResult.Failure(RiskEngineOutcomes.InvalidInput, code, message);

    private static int StableWindowSeed(RiskBacktestEngineInput input, int windowOffset, bool conservative)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{input.PortfolioId:N}:{input.From:yyyy-MM-dd}:{input.To:yyyy-MM-dd}:{windowOffset}:{conservative}"));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private sealed record BacktestWindowResult(
        DateOnly Date,
        decimal ActualReturn,
        decimal[] HistoricalVaR,
        decimal[] HistoricalEs,
        decimal[] MonteCarloVaR,
        decimal[] MonteCarloEs,
        decimal[] ConservativeVaR,
        decimal[] ConservativeEs);

    private static PortfolioRiskBacktestModelResponse BuildBacktestModel(
        string model,
        decimal confidence,
        IReadOnlyList<PortfolioRiskBacktestPoint> points)
    {
        var breaches = points.Count(point => point.Breached);
        var rate = points.Count == 0 ? 0 : (decimal)breaches / points.Count;
        var expected = 1 - confidence;
        decimal? kupiec = points.Count < 100 ? null : KupiecPValue(points.Count, breaches, expected);
        var christoffersen = points.Count < 100 ? null : ChristoffersenPValue(points);
        var tailPoints = points.Where(point => point.Breached).ToList();
        decimal? actualTailLossAverage = tailPoints.Count == 0 ? null : tailPoints.Average(point => point.ActualReturn);
        decimal? predictedEsAverage = tailPoints.Count == 0 ? null : tailPoints.Average(point => point.PredictedES);
        decimal? tailLossRatio = actualTailLossAverage is null || predictedEsAverage is null || predictedEsAverage == 0
            ? null : actualTailLossAverage / predictedEsAverage;
        var esStatus = tailLossRatio is null ? "insufficient_tail_observations"
            : tailLossRatio > 1.10m ? "underestimated" : tailLossRatio < 0.90m ? "conservative" : "aligned";
        return new(model, confidence, points.Count, breaches, rate, expected, kupiec, christoffersen,
            tailPoints.Count, actualTailLossAverage, predictedEsAverage, tailLossRatio, esStatus,
            points.Count < 100 ? "insufficient_observations" : "ready", points);
    }

    private static decimal KupiecPValue(int count, int breaches, decimal expected)
    {
        var observed = (decimal)breaches / count;
        Func<decimal, int, double> logLikelihood = (p, x) => x == 0
            ? count * Math.Log((double)(1 - p))
            : x * Math.Log((double)p) + (count - x) * Math.Log((double)(1 - p));
        var lr = -2d * (logLikelihood(expected, breaches) -
            logLikelihood(Math.Clamp(observed, 0.000001m, 0.999999m), breaches));
        return ChiSquareOneDegreeSurvival(lr);
    }

    private static decimal? ChristoffersenPValue(IReadOnlyList<PortfolioRiskBacktestPoint> points)
    {
        if (points.Count < 2) return null;
        var transitions = new int[2, 2];
        for (var index = 1; index < points.Count; index++)
            transitions[points[index - 1].Breached ? 1 : 0, points[index].Breached ? 1 : 0]++;
        var n0 = transitions[0, 0] + transitions[0, 1];
        var n1 = transitions[1, 0] + transitions[1, 1];
        if (n0 == 0 || n1 == 0) return null;
        var pi = (decimal)(transitions[0, 1] + transitions[1, 1]) / (n0 + n1);
        var pi0 = (decimal)transitions[0, 1] / n0;
        var pi1 = (decimal)transitions[1, 1] / n1;
        Func<decimal, int, int, double> ll = (p, a, b) =>
            a * Math.Log((double)(1 - Math.Clamp(p, 0.000001m, 0.999999m))) +
            b * Math.Log((double)Math.Clamp(p, 0.000001m, 0.999999m));
        var lr = -2d * (ll(pi, transitions[0, 0] + transitions[1, 0], transitions[0, 1] + transitions[1, 1]) -
            ll(pi0, transitions[0, 0], transitions[0, 1]) -
            ll(pi1, transitions[1, 0], transitions[1, 1]));
        return ChiSquareOneDegreeSurvival(lr);
    }

    private static decimal ChiSquareOneDegreeSurvival(double statistic) =>
        statistic <= 0 ? 1m : (decimal)Erfc(Math.Sqrt(statistic / 2d));

    private static double Erfc(double value)
    {
        var z = Math.Abs(value);
        var t = 1d / (1d + 0.5d * z);
        var answer = t * Math.Exp(-z * z - 1.26551223d +
            t * (1.00002368d + t * (0.37409196d + t * (0.09678418d +
            t * (-0.18628806d + t * (0.27886807d + t * (-1.13520398d +
            t * (1.48851587d + t * (-0.82215223d + t * 0.17087277d)))))))));
        return value >= 0 ? answer : 2d - answer;
    }
}
