using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.PortfolioTransactions;

public interface ITransactionRepository
{
    Task<IReadOnlyList<PortfolioTransaction>> ListAsync(Guid portfolioId, Guid? securityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PortfolioTransaction>> ListByHoldingAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken);
    Task<PortfolioTransaction?> GetAsync(Guid portfolioId, Guid transactionId, CancellationToken cancellationToken);
    void Add(PortfolioTransaction transaction);
    void Remove(PortfolioTransaction transaction);
}
