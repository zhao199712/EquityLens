using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.Portfolios;

public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly EquityLensDbContext _dbContext;

    public PortfolioRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PortfolioListItemResponse>> ListActiveAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Portfolios
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new PortfolioListItemResponse(
                x.Id,
                x.Name,
                x.Description,
                x.BaseCurrency,
                x.Holdings.Count,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<PortfolioDetailResponse?> GetDetailAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        return _dbContext.Portfolios
            .AsNoTracking()
            .Where(x => x.Id == id && x.OwnerUserId == ownerUserId && x.IsActive)
            .Select(x => new PortfolioDetailResponse(
                x.Id,
                x.Name,
                x.Description,
                x.BaseCurrency,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.Holdings
                    .OrderBy(h => h.Security.Ticker)
                    .Select(h => new PortfolioHoldingResponse(
                        h.Id,
                        h.SecurityId,
                        h.Security.Ticker,
                        h.Security.Exchange,
                        h.Security.Name,
                        h.Quantity,
                        h.AverageCost,
                        h.CostCurrency,
                        h.Note,
                        h.UpdatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Portfolio?> GetActiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        return _dbContext.Portfolios
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId && x.IsActive, cancellationToken);
    }

    public Task<bool> ActiveExistsAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        return _dbContext.Portfolios.AnyAsync(
            x => x.Id == id && x.OwnerUserId == ownerUserId && x.IsActive,
            cancellationToken);
    }

    public void Add(Portfolio portfolio)
    {
        _dbContext.Portfolios.Add(portfolio);
    }
}
