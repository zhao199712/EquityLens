using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

public static class RiskEngineOutcomes
{
    public const string Success = "Success";
    public const string InsufficientData = "InsufficientData";
    public const string InvalidInput = "InvalidInput";
    public const string NumericalFailure = "NumericalFailure";
}

public sealed record RiskBacktestEngineInput(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    int LookbackDays,
    int Simulations,
    IReadOnlyList<decimal> ConfidenceLevels,
    decimal EwmaLambda,
    decimal ShrinkageAlpha,
    decimal ConservativeResidualCapQuantile,
    IReadOnlyList<DateOnly> ReturnDates,
    IReadOnlyList<decimal> PortfolioReturns,
    IReadOnlyList<IReadOnlyList<decimal>> AssetReturns,
    IReadOnlyList<decimal> Weights);

public sealed record RiskBacktestEngineResult(
    string Outcome,
    PortfolioRiskBacktestResponse? Value,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static RiskBacktestEngineResult Success(PortfolioRiskBacktestResponse value) =>
        new(RiskEngineOutcomes.Success, value);

    public static RiskBacktestEngineResult Failure(string outcome, string errorCode, string errorMessage) =>
        new(outcome, null, errorCode, errorMessage);

    public Result<PortfolioRiskBacktestResponse> ToResult() =>
        Value is not null
            ? Result<PortfolioRiskBacktestResponse>.Success(Value)
            : Result<PortfolioRiskBacktestResponse>.Failure(
                ErrorCode ?? "risk.engine_failure",
                ErrorMessage ?? "Risk engine failed.");
}

public interface IRiskBacktestInputProvider
{
    Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        Guid providerUserId,
        CancellationToken cancellationToken);
}

public interface IRiskBacktestEngine
{
    string EngineName { get; }
    string AlgorithmVersion { get; }
    RiskBacktestEngineResult Calculate(RiskBacktestEngineInput input, CancellationToken cancellationToken = default);
}
