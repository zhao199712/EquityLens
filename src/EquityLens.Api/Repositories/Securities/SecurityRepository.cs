using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.Securities;

public sealed class SecurityRepository : ISecurityRepository
{
    private readonly EquityLensDbContext _dbContext;

    public SecurityRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var securitiesQuery = _dbContext.Securities
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim().ToUpperInvariant();
            securitiesQuery = securitiesQuery.Where(x =>
                x.Ticker.ToUpper().Contains(normalizedQuery) ||
                x.Exchange.ToUpper().Contains(normalizedQuery) ||
                x.Name.ToUpper().Contains(normalizedQuery));
        }

        return await securitiesQuery
            .OrderBy(x => x.Ticker)
            .ThenBy(x => x.Exchange)
            .Select(x => new SecurityResponse(
                x.Id,
                x.Ticker,
                x.Exchange,
                x.Name,
                x.AssetType,
                x.Currency,
                x.Isin,
                x.Sector,
                x.Industry))
            .ToListAsync(cancellationToken);
    }

    public Task<SecurityResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Securities
            .AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new SecurityResponse(
                x.Id,
                x.Ticker,
                x.Exchange,
                x.Name,
                x.AssetType,
                x.Currency,
                x.Isin,
                x.Sector,
                x.Industry))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Securities
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);
    }

    public Task<Security?> GetEntityByTickerExchangeAsync(string ticker, string exchange, CancellationToken cancellationToken)
    {
        return _dbContext.Securities
            .SingleOrDefaultAsync(x => x.Ticker == ticker && x.Exchange == exchange && x.IsActive, cancellationToken);
    }

    public Task<bool> ActiveExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Securities.AnyAsync(x => x.Id == id && x.IsActive, cancellationToken);
    }

    public Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken cancellationToken)
    {
        return _dbContext.Securities.AnyAsync(x => x.Ticker == ticker && x.Exchange == exchange, cancellationToken);
    }

    public void Add(Security security)
    {
        _dbContext.Securities.Add(security);
    }
}
