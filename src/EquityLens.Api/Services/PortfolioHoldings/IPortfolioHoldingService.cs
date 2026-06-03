using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;

namespace EquityLens.Api.Services.PortfolioHoldings;

public interface IPortfolioHoldingService
{
    Task<Result<IReadOnlyList<PortfolioHoldingResponse>>> ListAsync(Guid portfolioId, CancellationToken cancellationToken);
    Task<Result<PortfolioHoldingResponse>> CreateAsync(Guid portfolioId, CreatePortfolioHoldingRequest request, CancellationToken cancellationToken);
    Task<Result<bool>> UpdateAsync(Guid portfolioId, Guid holdingId, UpdatePortfolioHoldingRequest request, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken);
}
