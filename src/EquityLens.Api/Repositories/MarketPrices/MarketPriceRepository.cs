using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.MarketPrices;

public sealed class MarketPriceRepository : IMarketPriceRepository
{
    private readonly EquityLensDbContext _dbContext;

    public MarketPriceRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(
        IReadOnlyCollection<Guid> securityIds,
        string interval,
        CancellationToken cancellationToken)
    {
        if (securityIds.Count == 0)
        {
            return new Dictionary<Guid, LatestMarketPrice>();
        }

        var prices = await _dbContext.MarketPrices
            .AsNoTracking()
            .Where(x => securityIds.Contains(x.SecurityId) && x.Interval == interval)
            .OrderByDescending(x => x.PriceTime)
            .Select(x => new LatestMarketPrice(
                x.SecurityId,
                x.AdjustedClose ?? x.Close,
                x.PriceTime,
                x.DataSource))
            .ToListAsync(cancellationToken);

        return prices
            .GroupBy(x => x.SecurityId)
            .ToDictionary(x => x.Key, x => x.First());
    }
}
