using EquityLens.Api.Data;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data.Entities;
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

    public async Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(
        Guid securityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MarketPrices
            .AsNoTracking()
            .Where(x => x.SecurityId == securityId && x.Interval == "1d");

        if (from is not null)
        {
            var fromDateTime = ToUtcDateTime(from.Value);
            query = query.Where(x => x.PriceTime >= fromDateTime);
        }

        if (to is not null)
        {
            var toDateTime = ToUtcDateTime(to.Value);
            query = query.Where(x => x.PriceTime <= toDateTime);
        }

        return await query
            .OrderBy(x => x.PriceTime)
            .Select(x => new MarketPriceResponse(
                x.Id,
                x.SecurityId,
                x.PriceTime,
                x.Interval,
                x.Open,
                x.High,
                x.Low,
                x.Close,
                x.AdjustedClose,
                x.Volume,
                x.DataSource))
            .ToListAsync(cancellationToken);
    }

    public async Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(
        IReadOnlyList<MarketPrice> prices,
        CancellationToken cancellationToken)
    {
        if (prices.Count == 0)
        {
            return new UpsertMarketPricesResult(0, 0);
        }

        var securityId = prices[0].SecurityId;
        var priceTimes = prices.Select(x => x.PriceTime).ToList();
        var existingPrices = await _dbContext.MarketPrices
            .Where(x => x.SecurityId == securityId && x.Interval == "1d" && priceTimes.Contains(x.PriceTime))
            .ToDictionaryAsync(x => x.PriceTime, cancellationToken);

        var inserted = 0;
        var updated = 0;

        var now = DateTime.UtcNow;

        foreach (var price in prices)
        {
            if (existingPrices.TryGetValue(price.PriceTime, out var existing))
            {
                existing.Open = price.Open;
                existing.High = price.High;
                existing.Low = price.Low;
                existing.Close = price.Close;
                existing.AdjustedClose = price.AdjustedClose;
                existing.Volume = price.Volume;
                existing.DataSource = price.DataSource;
                existing.UpdatedAtUtc = now;
                updated++;
                continue;
            }

            price.UpdatedAtUtc = now;
            _dbContext.MarketPrices.Add(price);
            inserted++;
        }

        return new UpsertMarketPricesResult(inserted, updated);
    }

    private static DateTime ToUtcDateTime(DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}
