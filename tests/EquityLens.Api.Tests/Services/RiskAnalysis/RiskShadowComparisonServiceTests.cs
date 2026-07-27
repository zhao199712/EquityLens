using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskShadowComparisonServiceTests
{
    [Fact]
    public void Compare_ValuesWithinAbsoluteTolerance_Passes()
    {
        var primary = Response(-0.050m, -0.070m);
        var candidate = Response(-0.054m, -0.074m);

        var result = RiskShadowComparisonService.Compare(primary, candidate);

        Assert.Equal(1, result.ComparedPointCount);
        Assert.Equal(0, result.OutsideToleranceCount);
    }

    [Fact]
    public void Compare_MateriallyDifferentValues_ReportsDivergence()
    {
        var primary = Response(-0.050m, -0.070m);
        var candidate = Response(-0.080m, -0.110m);

        var result = RiskShadowComparisonService.Compare(primary, candidate);

        Assert.Equal(1, result.OutsideToleranceCount);
        Assert.Equal(0.030m, result.MaxAbsoluteVarDifference);
        Assert.Equal(0.040m, result.MaxAbsoluteEsDifference);
    }

    private static PortfolioRiskBacktestResponse Response(decimal valueAtRisk, decimal expectedShortfall)
    {
        var point = new PortfolioRiskBacktestPoint(
            new DateOnly(2026, 1, 2), -0.01m, valueAtRisk, expectedShortfall, false);
        var model = new PortfolioRiskBacktestModelResponse(
            "MVEWMA-FHS", 0.95m, 1, 0, 0, 0.05m, null, null, 0,
            null, null, null, "insufficient_tail_observations",
            "insufficient_observations", [point]);
        return new PortfolioRiskBacktestResponse(
            Guid.Empty, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1),
            252, 1, [model]);
    }
}
