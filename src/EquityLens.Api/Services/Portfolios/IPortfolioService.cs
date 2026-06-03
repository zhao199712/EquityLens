using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;

namespace EquityLens.Api.Services.Portfolios;

public interface IPortfolioService
{
    Task<IReadOnlyList<PortfolioListItemResponse>> ListAsync(CancellationToken cancellationToken);
    Task<Result<PortfolioDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<PortfolioDetailResponse>> CreateAsync(CreatePortfolioRequest request, CancellationToken cancellationToken);
    Task<Result<bool>> UpdateAsync(Guid id, UpdatePortfolioRequest request, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
