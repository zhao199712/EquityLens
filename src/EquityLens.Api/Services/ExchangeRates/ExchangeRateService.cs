using EquityLens.Api.Repositories.ExchangeRates;

namespace EquityLens.Api.Services.ExchangeRates;

public sealed class ExchangeRateService : IExchangeRateService
{
    private readonly IExchangeRateRepository _exchangeRateRepository;

    public ExchangeRateService(IExchangeRateRepository exchangeRateRepository)
    {
        _exchangeRateRepository = exchangeRateRepository;
    }

    public async Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        var rate = await GetRateAsync(fromCurrency, toCurrency, cancellationToken);
        return amount * rate;
    }

    public async Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var rate = await _exchangeRateRepository.GetRateAsync(fromCurrency, toCurrency, cancellationToken);
        if (rate is null)
            throw new InvalidOperationException($"Exchange rate not found for {fromCurrency} -> {toCurrency}");

        return rate.Value;
    }
}
