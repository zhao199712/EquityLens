using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

public interface ISecurityRiskCalculator
{
    Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
        Guid securityId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        CancellationToken cancellationToken);
}

public interface IPortfolioRiskCalculator
{
    Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        Guid providerUserId,
        CancellationToken cancellationToken,
        string modelName = "gbm_ewma_normal",
        IReadOnlyDictionary<Guid, decimal>? targetWeights = null);
}

public interface IPortfolioRiskBacktestService
{
    Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId,
        CancellationToken cancellationToken);
}

public interface IPortfolioMonteCarloService
{
    Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken,
        string modelName = "mvewma_fhs");
}

public interface IPortfolioRiskGovernanceService
{
    Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken);
}

public interface IPortfolioStressTestService
{
    Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken);

    Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestWithWeightsAsync(
        Guid portfolioId, Guid providerUserId, IReadOnlyDictionary<Guid, decimal> targetWeights,
        CancellationToken cancellationToken);
}

public interface IPortfolioRiskScenarioService
{
    Task<Result<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenarioAsync(Guid portfolioId, PortfolioRiskScenarioRequest request, Guid providerUserId, CancellationToken cancellationToken);
}

public interface IPortfolioRiskReportSnapshotService
{
    Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshotAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshotsAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken);

    Task<Result<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshotAsync(Guid portfolioId, Guid reportId, Guid providerUserId, CancellationToken cancellationToken);
}
