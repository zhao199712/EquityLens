using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>風險分析入口，將各項操作委派至專責元件。</summary>
public sealed class RiskAnalysisService(
    ISecurityRiskCalculator security,
    IPortfolioRiskCalculator portfolio,
    IPortfolioRiskBacktestService backtest,
    IPortfolioMonteCarloService monteCarlo,
    IPortfolioRiskGovernanceService governance,
    IPortfolioStressTestService stress,
    IPortfolioRiskScenarioService scenario,
    IPortfolioRiskReportSnapshotService reports) : IRiskAnalysisService
{
    /// <inheritdoc />
    public Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
        Guid securityId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        CancellationToken cancellationToken) =>
        security.GetSecurityRiskAsync(securityId, from, to, horizonDays, confidenceLevel, simulations, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        Guid providerUserId,
        CancellationToken cancellationToken,
        string modelName = "gbm_ewma_normal",
        IReadOnlyDictionary<Guid, decimal>? targetWeights = null) =>
        portfolio.GetPortfolioRiskAsync(portfolioId, from, to, horizonDays, confidenceLevel, simulations, providerUserId, cancellationToken, modelName, targetWeights);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId,
        CancellationToken cancellationToken) =>
        backtest.GetPortfolioRiskBacktestAsync(portfolioId, from, to, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken,
        string modelName = "mvewma_fhs") =>
        monteCarlo.GetPortfolioMonteCarloAsync(portfolioId, providerUserId, cancellationToken, modelName);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        governance.GetPortfolioRiskGovernanceAsync(portfolioId, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        stress.GetPortfolioStressTestAsync(portfolioId, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenarioAsync(Guid portfolioId, PortfolioRiskScenarioRequest request, Guid providerUserId, CancellationToken cancellationToken) =>
        scenario.CalculatePortfolioRiskScenarioAsync(portfolioId, request, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshotAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        reports.CreatePortfolioRiskReportSnapshotAsync(portfolioId, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshotsAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        reports.GetPortfolioRiskReportSnapshotsAsync(portfolioId, providerUserId, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshotAsync(Guid portfolioId, Guid reportId, Guid providerUserId, CancellationToken cancellationToken) =>
        reports.GetPortfolioRiskReportSnapshotAsync(portfolioId, reportId, providerUserId, cancellationToken);
}
