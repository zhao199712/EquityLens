namespace EquityLens.Api.Services.PortfolioFunding;

public interface IPortfolioFundingService
{
    Task RebuildImplicitFundingAsync(Guid portfolioId, CancellationToken cancellationToken);
}
