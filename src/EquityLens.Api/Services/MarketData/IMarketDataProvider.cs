using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.MarketData;

public interface IMarketDataProvider
{
    string SourceName { get; }

    bool Supports(string exchange);

    Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
