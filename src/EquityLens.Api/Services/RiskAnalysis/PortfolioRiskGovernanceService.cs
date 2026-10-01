using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>根據正式風險資料判定治理狀態。</summary>
public sealed class PortfolioRiskGovernanceService(
    IPortfolioRiskCalculator riskCalculator,
    RiskOperationTelemetry telemetry) : IPortfolioRiskGovernanceService
{
    public async Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        await telemetry.ExecuteAsync(
            "risk.portfolio.governance", MvewmaFhsModel, .95m, 5000,
            () => GetPortfolioRiskGovernanceCoreAsync(portfolioId, providerUserId, cancellationToken));

    private async Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceCoreAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var risk = await riskCalculator.GetPortfolioRiskAsync(portfolioId, to.AddYears(-1), to, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel);
        if (!risk.IsSuccess) return Result<PortfolioRiskGovernanceResponse>.Failure("risk.insufficient_prices", "Risk governance cannot be evaluated because formal risk data is unavailable.");
        return Result<PortfolioRiskGovernanceResponse>.Success(RiskGovernanceEvaluator.Evaluate(portfolioId, risk.Value!, 0m, to));
    }
}
