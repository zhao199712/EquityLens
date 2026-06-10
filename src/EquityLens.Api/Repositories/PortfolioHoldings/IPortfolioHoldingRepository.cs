using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.PortfolioHoldings;

public interface IPortfolioHoldingRepository
{
    Task<IReadOnlyList<PortfolioHoldingResponse>> ListAsync(Guid portfolioId, CancellationToken cancellationToken);
    Task<PortfolioHoldingResponse?> GetResponseAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken);
    Task<PortfolioHolding?> GetAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken);
    Task<PortfolioHolding?> GetBySecurityIdAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken);
    Task<bool> SecurityHoldingExistsAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken);
    void Add(PortfolioHolding holding);
    void Remove(PortfolioHolding holding);
}
