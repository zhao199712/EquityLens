using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;

namespace EquityLens.Api.Services.PortfolioValuations;

public interface IPortfolioValuationService
{
    Task<Result<PortfolioValuationResponse>> GetValuationAsync(Guid portfolioId, CancellationToken cancellationToken);
}
