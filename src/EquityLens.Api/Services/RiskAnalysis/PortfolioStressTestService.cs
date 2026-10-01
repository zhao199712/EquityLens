using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.ExchangeRates;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>準備目前或目標權重的壓力測試資料。</summary>
public sealed class PortfolioStressTestService(
    IPortfolioRepository portfolios,
    IMarketPriceRepository pricesRepository,
    IExchangeRateService exchangeRates,
    RiskOperationTelemetry telemetry) : IPortfolioStressTestService
{
    public async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        await telemetry.ExecuteAsync(
            "risk.portfolio.stress", null, null, null,
            () => GetPortfolioStressTestCoreAsync(portfolioId, providerUserId, cancellationToken));

    private async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestCoreAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        var portfolio = await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioStressTestResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<PortfolioStressTestResponse>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");
        var ids = portfolio.Holdings.Select(x => x.SecurityId).Distinct().ToList();
        var latest = await pricesRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        var values = await Task.WhenAll(portfolio.Holdings.Where(h => latest.ContainsKey(h.SecurityId)).Select(async h => (h, Value: await exchangeRates.ConvertAsync(PortfolioMath.CalculateMarketValue(h.Quantity, latest[h.SecurityId].Price), h.CostCurrency, portfolio.BaseCurrency, cancellationToken))));
        var total = values.Sum(x => x.Value);
        if (total <= 0) return Result<PortfolioStressTestResponse>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = values.ToDictionary(x => x.h.SecurityId, x => x.Value / total);
        var prices = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var id in ids) prices[id] = await pricesRepository.GetBySecurityAsync(id, null, null, cancellationToken);
        var scenarios = PortfolioStressScenarioCalculator.Calculate(
            portfolio.Holdings, weights, prices, latest.ToDictionary(x => x.Key, x => x.Value.Price));
        return Result<PortfolioStressTestResponse>.Success(new(portfolioId, latest.Values.MaxBy(x => x.PriceTime)?.PriceTime is { } date ? DateOnly.FromDateTime(date) : null, scenarios));
    }

    public async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestWithWeightsAsync(Guid portfolioId, Guid providerUserId, IReadOnlyDictionary<Guid, decimal> targetWeights, CancellationToken cancellationToken)
    {
        var portfolio = await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioStressTestResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        var ids = portfolio.Holdings.Select(x => x.SecurityId).Distinct().ToList(); var latest = await pricesRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        if (latest.Count != ids.Count) return Result<PortfolioStressTestResponse>.Failure("risk.insufficient_prices", "All holdings require latest prices.");
        var prices = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>(); foreach (var id in ids) prices[id] = await pricesRepository.GetBySecurityAsync(id, null, null, cancellationToken);
        var scenarios = PortfolioStressScenarioCalculator.Calculate(
            portfolio.Holdings, targetWeights, prices, latest.ToDictionary(x => x.Key, x => x.Value.Price));
        return Result<PortfolioStressTestResponse>.Success(new(portfolioId, DateOnly.FromDateTime(latest.Values.MaxBy(x => x.PriceTime)!.PriceTime), scenarios));
    }
}
