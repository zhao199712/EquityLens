using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Domain.Calculations;

using static EquityLens.Api.Services.RiskAnalysis.PortfolioRiskModelCalculator;
using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>計算年度投組模擬路徑與診斷資料。</summary>
public sealed class PortfolioMonteCarloService(
    PortfolioRiskDataPreparer data,
    RiskOperationTelemetry telemetry) : IPortfolioMonteCarloService
{
    public async Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken, string modelName = MvewmaFhsModel) =>
        await telemetry.ExecuteAsync(
            "risk.portfolio.monte-carlo", modelName, null, 10000,
            () => GetPortfolioMonteCarloCoreAsync(portfolioId, providerUserId, cancellationToken, modelName));

    private async Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloCoreAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken, string modelName = MvewmaFhsModel)
    {
        const int horizonDays = 252;
        const int simulations = 10000;
        const int samplePathCount = 8;
        if (modelName is not (MvewmaFhsModel or ConservativeMvewmaFhsModel))
            return Result<PortfolioMonteCarloResponse>.Failure("risk.invalid_model", "Unsupported Monte Carlo model.");
        var residualCapQuantile = modelName == ConservativeMvewmaFhsModel ? ConservativeResidualCapQuantile : 0m;
        var displayModel = residualCapQuantile > 0 ? "MVEWMA-FHS（保守 p99）" : "MVEWMA-FHS";
        PortfolioMonteCarloResponse Insufficient(string message, DateOnly? asOf = null, int commonDays = 0) =>
            new(portfolioId, "insufficient_prices", message, asOf, commonDays, horizonDays, simulations,
                displayModel, EwmaLambda, 0, residualCapQuantile, 0, Array.Empty<PortfolioMonteCarloBandPoint>(),
                Array.Empty<PortfolioMonteCarloPath>(), 0, 0, 0, 0,
                new PortfolioMonteCarloDiagnostics(0, 0, 0, 0, 0, 0, 0, residualCapQuantile, 0, false, null));

        var prepared = await data.PrepareMonteCarloAsync(portfolioId, providerUserId, cancellationToken);
        if (!prepared.IsSuccess)
            return Result<PortfolioMonteCarloResponse>.Failure(prepared.ErrorCode!, prepared.ErrorMessage!);
        var input = prepared.Value!;
        if (input.Data is null)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient(
                input.InsufficientMessage!, input.DataAsOf, input.CommonTradingDays));
        var alignedPrices = input.Data.AlignedPrices;
        var weights = input.Data.Weights;
        var dataAsOf = input.DataAsOf!.Value;
        var commonTradingDays = input.CommonTradingDays;
        var securityCount = alignedPrices.Count;

        var (returnMatrix, weightList) = BuildReturnMatrix(alignedPrices, weights);
        if (returnMatrix.Count != securityCount || returnMatrix.Count == 0)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient("無法建立所有持倉的有效共同日報酬。", dataAsOf, commonTradingDays));
        var shrinkage = RiskMath.DetermineAutoShrinkageAlpha(returnMatrix.Count, returnMatrix[0].Count);
        var seed = BuildMonteCarloSeed(portfolioId, dataAsOf, horizonDays, simulations);
        var simulation = RiskMath.RunMultivariateFhsPathSimulation(
            returnMatrix, weightList, horizonDays, simulations, samplePathCount, seed, EwmaLambda, shrinkage, residualCapQuantile);
        if (simulation.DidFallback)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient("共同日報酬不足，無法完成 MVEWMA-FHS 模擬。", dataAsOf, commonTradingDays));

        var bands = simulation.Bands.Select(band => new PortfolioMonteCarloBandPoint(
            band.Day, band.P1, band.P5, band.P50, band.P95, band.P99)).ToList();
        var paths = simulation.SamplePaths.Select((path, index) => new PortfolioMonteCarloPath(index + 1, path)).ToList();
        var rightSkewWarning = RiskMath.HasMaterialRightSkew(simulation.ExpectedMedianGap);
        var diagnostics = new PortfolioMonteCarloDiagnostics(
            simulation.AnnualizedPortfolioVolatility,
            simulation.ResidualNormP99,
            simulation.MaxResidualNorm,
            simulation.P50FinalReturn,
            simulation.P95FinalReturn,
            simulation.P99FinalReturn,
            simulation.ExpectedMedianGap,
            simulation.ResidualCapQuantile,
            simulation.CappedDrawRate,
            rightSkewWarning,
            rightSkewWarning
                ? "期望期末報酬明顯高於中位數，表示平均值受到少數高報酬路徑拉高；請優先參考中位數與下行情境。"
                : null);
        return Result<PortfolioMonteCarloResponse>.Success(new(
            portfolioId, "ready", null, dataAsOf, simulation.CommonTradingDays, horizonDays, simulations,
            displayModel, EwmaLambda, simulation.ShrinkageAlpha, simulation.ResidualCapQuantile,
            simulation.CappedDrawRate, bands, paths,
            simulation.PositiveReturnProbability, simulation.ExpectedReturn,
            simulation.P5FinalReturn, simulation.P1FinalReturn, diagnostics));
    }

    private static int BuildMonteCarloSeed(Guid portfolioId, DateOnly dataAsOf, int horizonDays, int simulations)
    {
        var bytes = portfolioId.ToByteArray();
        var seed = 17;
        foreach (var value in bytes) seed = unchecked(seed * 31 + value);
        seed = unchecked(seed * 31 + dataAsOf.DayNumber);
        seed = unchecked(seed * 31 + horizonDays);
        return unchecked(seed * 31 + simulations);
    }
}
