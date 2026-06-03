namespace EquityLens.Api.Repositories.MarketPrices;

public interface IMarketPriceRepository
{
    Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(
        IReadOnlyCollection<Guid> securityIds,
        string interval,
        CancellationToken cancellationToken);
}
