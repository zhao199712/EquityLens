using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.PortfolioTransactions;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly EquityLensDbContext _dbContext;

    public TransactionRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PortfolioTransaction>> ListAsync(
        Guid portfolioId, Guid? securityId, CancellationToken cancellationToken)
    {
        var query = _dbContext.PortfolioTransactions
            .AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId);

        if (securityId.HasValue)
            query = query.Where(x => x.SecurityId == securityId.Value);

        return await query
            .OrderByDescending(x => x.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PortfolioTransaction>> ListByHoldingAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        return await _dbContext.PortfolioTransactions
            .AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.SecurityId == securityId)
            .OrderBy(x => x.TransactionDate)
            .ToListAsync(cancellationToken);
    }

    public Task<PortfolioTransaction?> GetAsync(Guid portfolioId, Guid transactionId, CancellationToken cancellationToken)
    {
        return _dbContext.PortfolioTransactions
            .FirstOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Id == transactionId, cancellationToken);
    }

    public void Add(PortfolioTransaction transaction)
    {
        _dbContext.PortfolioTransactions.Add(transaction);
    }

    public void Remove(PortfolioTransaction transaction)
    {
        _dbContext.PortfolioTransactions.Remove(transaction);
    }
}
