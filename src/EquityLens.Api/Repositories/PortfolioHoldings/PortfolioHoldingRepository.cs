using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.PortfolioHoldings;

public sealed class PortfolioHoldingRepository : IPortfolioHoldingRepository
{
    private readonly EquityLensDbContext _dbContext;

    public PortfolioHoldingRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PortfolioHoldingResponse>> ListAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        return await _dbContext.PortfolioHoldings
            .AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId)
            .OrderBy(x => x.Security.Ticker)
            .Select(x => new PortfolioHoldingResponse(
                x.Id,
                x.SecurityId,
                x.Security.Ticker,
                x.Security.Exchange,
                x.Security.Name,
                x.Quantity,
                x.AverageCost,
                x.CostCurrency,
                x.Note,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<PortfolioHoldingResponse?> GetResponseAsync(
        Guid portfolioId,
        Guid holdingId,
        CancellationToken cancellationToken)
    {
        return _dbContext.PortfolioHoldings
            .AsNoTracking()
            .Where(x => x.Id == holdingId && x.PortfolioId == portfolioId)
            .Select(x => new PortfolioHoldingResponse(
                x.Id,
                x.SecurityId,
                x.Security.Ticker,
                x.Security.Exchange,
                x.Security.Name,
                x.Quantity,
                x.AverageCost,
                x.CostCurrency,
                x.Note,
                x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PortfolioHolding?> GetAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken)
    {
        return _dbContext.PortfolioHoldings
            .SingleOrDefaultAsync(x => x.Id == holdingId && x.PortfolioId == portfolioId, cancellationToken);
    }

    public Task<PortfolioHolding?> GetBySecurityIdAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        return _dbContext.PortfolioHoldings
            .SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.SecurityId == securityId, cancellationToken);
    }

    public Task<bool> SecurityHoldingExistsAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        return _dbContext.PortfolioHoldings
            .AnyAsync(x => x.PortfolioId == portfolioId && x.SecurityId == securityId, cancellationToken);
    }

    public void Add(PortfolioHolding holding)
    {
        _dbContext.PortfolioHoldings.Add(holding);
    }

    public void Remove(PortfolioHolding holding)
    {
        _dbContext.PortfolioHoldings.Remove(holding);
    }
}
