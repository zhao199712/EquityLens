namespace EquityLens.Api.Repositories.ExchangeRates;

public interface IExchangeRateRepository
{
    Task<decimal?> GetRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken);
}
