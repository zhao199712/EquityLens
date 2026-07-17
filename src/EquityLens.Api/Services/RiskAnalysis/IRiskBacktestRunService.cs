using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

public interface IRiskBacktestRunService
{
    Task<Result<PortfolioRiskBacktestRunResponse>> CreateAsync(Guid portfolioId, DateOnly from, DateOnly to, Guid userId, CancellationToken cancellationToken = default);
    Task<Result<PortfolioRiskBacktestRunResponse>> GetAsync(Guid portfolioId, Guid runId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PortfolioRiskBacktestRunResponse>> ListAsync(Guid portfolioId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExecuteAsync(Guid runId, CancellationToken cancellationToken = default);
}
