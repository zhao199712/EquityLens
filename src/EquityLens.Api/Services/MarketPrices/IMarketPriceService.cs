using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;

namespace EquityLens.Api.Services.MarketPrices;

public interface IMarketPriceService
{
    Task<Result<IReadOnlyList<MarketPriceResponse>>> GetPricesAsync(
        Guid securityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<Result<ImportMarketPricesResponse>> ImportDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken);

    Task<Result<ImportMarketPricesResponse>> SyncDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken);

    Task<Result<ImportMarketPricesByTickerResponse>> ImportDailyPricesByTickerAsync(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken);

}
