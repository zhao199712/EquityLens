using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.MarketPrices;

public interface IMarketPriceRepository
{
    Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(
        IReadOnlyCollection<Guid> securityIds,
        string interval,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(
        Guid securityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(
        IReadOnlyList<MarketPrice> prices,
        CancellationToken cancellationToken);
}
