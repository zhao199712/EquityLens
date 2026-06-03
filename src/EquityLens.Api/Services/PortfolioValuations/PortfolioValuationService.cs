using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.DemoUser;

namespace EquityLens.Api.Services.PortfolioValuations;

public sealed class PortfolioValuationService : IPortfolioValuationService
{
    private const string DailyInterval = "1d";

    private readonly IDemoUserContext _demoUserContext;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;

    public PortfolioValuationService(
        IDemoUserContext demoUserContext,
        IPortfolioRepository portfolioRepository,
        IMarketPriceRepository marketPriceRepository)
    {
        _demoUserContext = demoUserContext;
        _portfolioRepository = portfolioRepository;
        _marketPriceRepository = marketPriceRepository;
    }

    public async Task<Result<PortfolioValuationResponse>> GetValuationAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(
            portfolioId,
            _demoUserContext.UserId,
            cancellationToken);

        if (portfolio is null)
        {
            return Result<PortfolioValuationResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var securityIds = portfolio.Holdings
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        var latestPrices = await _marketPriceRepository.GetLatestPricesAsync(
            securityIds,
            DailyInterval,
            cancellationToken);

        var holdingDrafts = portfolio.Holdings
            .Select(holding =>
            {
                var costValue = PortfolioMath.CalculateCostValue(holding.Quantity, holding.AverageCost);
                if (!latestPrices.TryGetValue(holding.SecurityId, out var latestPrice))
                {
                    return new HoldingValuationDraft(
                        holding.Id,
                        holding.SecurityId,
                        holding.Ticker,
                        holding.Exchange,
                        holding.SecurityName,
                        holding.Quantity,
                        holding.AverageCost,
                        holding.CostCurrency,
                        null,
                        null,
                        costValue,
                        null,
                        null,
                        null,
                        "MissingPrice");
                }

                var marketValue = PortfolioMath.CalculateMarketValue(holding.Quantity, latestPrice.Price);
                var unrealizedPnl = PortfolioMath.CalculateUnrealizedPnl(marketValue, costValue);
                var unrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(unrealizedPnl, costValue);

                return new HoldingValuationDraft(
                    holding.Id,
                    holding.SecurityId,
                    holding.Ticker,
                    holding.Exchange,
                    holding.SecurityName,
                    holding.Quantity,
                    holding.AverageCost,
                    holding.CostCurrency,
                    latestPrice.Price,
                    latestPrice.PriceTime,
                    costValue,
                    marketValue,
                    unrealizedPnl,
                    unrealizedPnlPercent,
                    "Priced");
            })
            .ToList();

        var totalCostValue = holdingDrafts.Sum(x => x.CostValue);
        var totalMarketValue = holdingDrafts.Sum(x => x.MarketValue ?? 0);
        var pricedCostValue = holdingDrafts.Where(x => x.MarketValue.HasValue).Sum(x => x.CostValue);
        var totalUnrealizedPnl = holdingDrafts.Sum(x => x.UnrealizedPnl ?? 0);
        var totalUnrealizedPnlPercent = PortfolioMath.CalculateUnrealizedPnlPercent(totalUnrealizedPnl, pricedCostValue);

        var holdings = holdingDrafts
            .Select(x => new HoldingValuationResponse(
                x.HoldingId,
                x.SecurityId,
                x.Ticker,
                x.Exchange,
                x.SecurityName,
                x.Quantity,
                x.AverageCost,
                x.CostCurrency,
                x.LatestPrice,
                x.PriceTime,
                x.CostValue,
                x.MarketValue,
                x.UnrealizedPnl,
                x.UnrealizedPnlPercent,
                x.MarketValue.HasValue ? PortfolioMath.CalculateWeight(x.MarketValue.Value, totalMarketValue) : null,
                x.ValuationStatus))
            .ToList();

        var response = new PortfolioValuationResponse(
            portfolio.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            portfolio.BaseCurrency,
            totalCostValue,
            totalMarketValue,
            totalUnrealizedPnl,
            totalUnrealizedPnlPercent,
            holdings);

        return Result<PortfolioValuationResponse>.Success(response);
    }

    private sealed record HoldingValuationDraft(
        Guid HoldingId,
        Guid SecurityId,
        string Ticker,
        string Exchange,
        string SecurityName,
        decimal Quantity,
        decimal AverageCost,
        string CostCurrency,
        decimal? LatestPrice,
        DateTime? PriceTime,
        decimal CostValue,
        decimal? MarketValue,
        decimal? UnrealizedPnl,
        decimal? UnrealizedPnlPercent,
        string ValuationStatus);
}
