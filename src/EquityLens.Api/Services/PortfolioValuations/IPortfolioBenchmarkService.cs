namespace EquityLens.Api.Services.PortfolioValuations;
public interface IPortfolioBenchmarkService
{
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetTotalReturnIndexAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
