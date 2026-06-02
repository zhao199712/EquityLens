using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.Portfolios;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<PortfolioListItemResponse>> ListActiveAsync(Guid ownerUserId, CancellationToken cancellationToken);
    Task<PortfolioDetailResponse?> GetDetailAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);
    Task<Portfolio?> GetActiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);
    Task<bool> ActiveExistsAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken);
    void Add(Portfolio portfolio);
}
