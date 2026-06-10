using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.ExchangeRates;

public sealed class ExchangeRateRepository : IExchangeRateRepository
{
    private readonly EquityLensDbContext _dbContext;

    public ExchangeRateRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<decimal?> GetRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken)
    {
        if (string.Equals(sourceCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var rate = await _dbContext.ExchangeRates
            .AsNoTracking()
            .Where(x => x.SourceCurrency == sourceCurrency && x.TargetCurrency == targetCurrency)
            .Select(x => x.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        return rate == 0 ? null : rate;
    }
}
