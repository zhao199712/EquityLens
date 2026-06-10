namespace EquityLens.Api.Services.ExchangeRates;

public interface IExchangeRateService
{
    Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken);
    Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken);
}
