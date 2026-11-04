using System.Diagnostics;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Observability;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.ExchangeRates;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>
/// 準備投組風險資料。三個入口各自保留正式風險、嚴格回測及年度模擬的資料政策。
/// </summary>
public sealed class PortfolioRiskDataPreparer(
    IPortfolioRepository portfolioRepository,
    IMarketPriceRepository marketPriceRepository,
    IExchangeRateService exchangeRateService)
{
    public async Task<Result<PortfolioRiskPreparedData>> PreparePortfolioRiskAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        Guid providerUserId,
        CancellationToken cancellationToken,
        string modelName = "gbm_ewma_normal",
        IReadOnlyDictionary<Guid, decimal>? targetWeights = null)
    {
        if (from >= to)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.invalid_date_range", "'from' must be earlier than 'to'.");
        }

        if (confidenceLevel < MinConfidenceLevel || confidenceLevel > MaxConfidenceLevel)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.invalid_confidence_level", "Confidence level must be between 0.90 and 0.999.");
        }

        if (simulations < MinSimulations || simulations > MaxSimulations)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.invalid_simulations", "Simulations must be between 1000 and 100000.");
        }

        if (modelName is not ("gbm_ewma_normal" or MvewmaFhsModel or ConservativeMvewmaFhsModel))
            return Result<PortfolioRiskPreparedData>.Failure("risk.invalid_model", "Unsupported risk model.");

        var portfolio = await portfolioRepository.GetDetailAsync(
            portfolioId, providerUserId, cancellationToken);

        if (portfolio is null)
        {
            return Result<PortfolioRiskPreparedData>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        if (portfolio.Holdings.Count == 0)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "portfolio.no_holdings", "Portfolio has no holdings.");
        }

        var securityIds = portfolio.Holdings
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, LatestMarketPrice> latestPrices;
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "prices.load"))
        {
            latestPrices = await marketPriceRepository.GetLatestPricesAsync(
                securityIds, DailyInterval, cancellationToken);
        }

        if (latestPrices.Count == 0)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.insufficient_prices", "No latest prices available for any holding.");
        }

        var pricedHoldings = portfolio.Holdings
            .Where(h => latestPrices.ContainsKey(h.SecurityId))
            .ToList();

        if (pricedHoldings.Count == 0)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.insufficient_prices", "No latest prices available for any holding.");
        }

        var pricedSecurityIds = pricedHoldings
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        // 計算各持倉市值與權重
        var marketValueTasks = pricedHoldings.Select(async h =>
        {
            var latestPrice = latestPrices[h.SecurityId];
            var localMarketValue = PortfolioMath.CalculateMarketValue(h.Quantity, latestPrice.Price);
            var marketValueInBase = await exchangeRateService.ConvertAsync(
                localMarketValue, h.CostCurrency, portfolio.BaseCurrency, cancellationToken);
            return (Holding: h, MarketValueInBase: marketValueInBase);
        });

        var marketValues = await Task.WhenAll(marketValueTasks);
        var totalMarketValue = marketValues.Sum(x => x.MarketValueInBase);

        if (totalMarketValue <= 0)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.invalid_market_value", "Total market value is zero or negative.");
        }

        // 取得各證券歷史價格
        var pricesBySecurity = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        var priceCountsBySecurity = new Dictionary<Guid, int>();
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "prices.load"))
        {
            foreach (var securityId in pricedSecurityIds)
            {
                var prices = await marketPriceRepository.GetBySecurityAsync(
                    securityId, from, to, cancellationToken);
                priceCountsBySecurity[securityId] = prices.Count;
                if (prices.Count >= MinPriceCount)
                {
                    pricesBySecurity[securityId] = prices;
                }
            }
        }

        if (pricesBySecurity.Count == 0)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.insufficient_prices",
                $"No holding has the required {MinPriceCount} price records; " +
                $"available records range from {priceCountsBySecurity.Values.Min()} to {priceCountsBySecurity.Values.Max()}.");
        }

        // 日期對齊：只保留所有資產共同的交易日
        List<DateOnly> commonDates;
        Dictionary<Guid, IReadOnlyList<decimal>> dateToPrice;
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "returns.align"))
        {
            var dateSets = pricesBySecurity.Values
                .Select(p => p.Select(x => DateOnly.FromDateTime(x.PriceTime)).ToHashSet())
                .ToList();

            commonDates = dateSets.Skip(1)
                .Aggregate(dateSets[0], (intersection, set) =>
                {
                    intersection.IntersectWith(set);
                    return intersection;
                })
                .OrderBy(d => d)
                .ToList();

            dateToPrice = pricesBySecurity.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<decimal>)kvp.Value
                    .Where(p => commonDates.Contains(DateOnly.FromDateTime(p.PriceTime)))
                    .OrderBy(p => p.PriceTime)
                    .Select(p => p.AdjustedClose ?? p.Close)
                    .ToList());
        }

        if (commonDates.Count < MinPriceCount)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.insufficient_prices",
                $"After date alignment, only {commonDates.Count} common trading days remain " +
                $"(need at least {MinPriceCount}).");
        }

        if (dateToPrice.Values.Any(prices => prices.Any(price => price <= 0)))
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.non_positive_price",
                "Historical prices contain non-positive values, cannot estimate portfolio risk.");
        }

        // 計算各資產權重
        var weightsBySecurityId = marketValues.ToDictionary(
            x => x.Holding.SecurityId,
            x => x.MarketValueInBase / totalMarketValue);
        if (targetWeights is not null)
            weightsBySecurityId = weightsBySecurityId.Keys.ToDictionary(id => id, id => targetWeights.GetValueOrDefault(id, 0m));

        // 計算每日 portfolio return
        var portfolioReturns = new List<decimal>(commonDates.Count);
        for (var d = 1; d < commonDates.Count; d++)
        {
            var dailyReturn = 0m;
            foreach (var kvp in dateToPrice)
            {
                var weight = weightsBySecurityId.GetValueOrDefault(kvp.Key, 0);
                if (weight == 0) continue;

                var logReturn = (decimal)Math.Log(
                    (double)(kvp.Value[d] / kvp.Value[d - 1]));
                dailyReturn += weight * logReturn;
            }
            portfolioReturns.Add(dailyReturn);
        }

        if (portfolioReturns.Count < 2)
        {
            return Result<PortfolioRiskPreparedData>.Failure(
                "risk.insufficient_prices", "Not enough aligned return data points.");
        }

        return Result<PortfolioRiskPreparedData>.Success(new(
            portfolio, pricedHoldings, marketValues, totalMarketValue, commonDates,
            dateToPrice, weightsBySecurityId, portfolioReturns));
    }

    public async Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken)
    {
        const int lookbackDays = 252;
        const int minimumObservations = 100;
        const int simulations = 5000;
        if (from >= to) return Result<RiskBacktestEngineInput>.Failure("risk.invalid_date_range", "'from' must be earlier than 'to'.");

        var portfolio = await portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<RiskBacktestEngineInput>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<RiskBacktestEngineInput>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");

        var ids = portfolio.Holdings.Select(h => h.SecurityId).Distinct().ToList();
        var latest = await marketPriceRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        var holdings = portfolio.Holdings.Where(h => latest.ContainsKey(h.SecurityId)).ToList();
        if (holdings.Count == 0) return Result<RiskBacktestEngineInput>.Failure("risk.insufficient_prices", "No latest prices available for portfolio holdings.");

        var values = await Task.WhenAll(holdings.Select(async h =>
        {
            var localValue = PortfolioMath.CalculateMarketValue(h.Quantity, latest[h.SecurityId].Price);
            return (h.SecurityId, Value: await exchangeRateService.ConvertAsync(localValue, h.CostCurrency, portfolio.BaseCurrency, cancellationToken));
        }));
        var total = values.Sum(v => v.Value);
        if (total <= 0) return Result<RiskBacktestEngineInput>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = values.ToDictionary(v => v.SecurityId, v => v.Value / total);

        var pricesById = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var id in weights.Keys)
            pricesById[id] = await marketPriceRepository.GetBySecurityAsync(id, from, to, cancellationToken);
        var dateSets = pricesById.Values.Select(ps => ps.Select(p => DateOnly.FromDateTime(p.PriceTime)).ToHashSet()).ToList();
        var commonDates = dateSets.Skip(1).Aggregate(dateSets[0], (set, next) => { set.IntersectWith(next); return set; }).OrderBy(d => d).ToList();
        if (commonDates.Count < lookbackDays + minimumObservations)
            return Result<RiskBacktestEngineInput>.Failure("risk.insufficient_prices", $"At least {lookbackDays + minimumObservations} common trading days are required for backtesting (got {commonDates.Count}).");

        var alignedRows = pricesById.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Where(p => commonDates.Contains(DateOnly.FromDateTime(p.PriceTime)))
                .OrderBy(p => p.PriceTime)
                .ToList());
        if (alignedRows.Values.Any(rows => rows.Any(p => p.AdjustedClose is null)))
            return Result<RiskBacktestEngineInput>.Failure(
                "risk.incomplete_adjusted_close",
                "Complete adjusted-close coverage is required; raw close is never used as a silent substitute.");
        var aligned = alignedRows.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(p => p.AdjustedClose!.Value).ToList());
        if (aligned.Values.Any(ps => ps.Any(p => p <= 0))) return Result<RiskBacktestEngineInput>.Failure("risk.non_positive_price", "Historical prices contain non-positive values.");

        var returns = new List<decimal>(commonDates.Count - 1);
        for (var index = 1; index < commonDates.Count; index++)
            returns.Add(aligned.Sum(pair => weights[pair.Key] * (decimal)Math.Log((double)(pair.Value[index] / pair.Value[index - 1]))));

        var assetIds = aligned.Keys.OrderBy(id => id).ToArray();
        var weightList = assetIds.Select(id => weights[id]).ToArray();
        var shrinkageAlpha = RiskMath.DetermineAutoShrinkageAlpha(assetIds.Length, lookbackDays);
        var portfolioReturns = returns.ToArray();
        var confidenceLevels = new[] { 0.95m, 0.99m };
        var assetReturns = assetIds.Select(id =>
        {
            var prices = aligned[id];
            var result = new decimal[prices.Count - 1];
            for (var index = 0; index < result.Length; index++)
                result[index] = (decimal)Math.Log((double)(prices[index + 1] / prices[index]));
            return result;
        }).ToArray();

        return Result<RiskBacktestEngineInput>.Success(new RiskBacktestEngineInput(
            portfolioId,
            from,
            to,
            lookbackDays,
            simulations,
            confidenceLevels,
            EwmaLambda,
            shrinkageAlpha,
            ConservativeResidualCapQuantile,
            commonDates.Skip(1).ToArray(),
            portfolioReturns,
            assetReturns,
            weightList,
            assetIds.Select(id =>
            {
                var holding = holdings.Single(h => h.SecurityId == id);
                var industry = IndustryClassification.Normalize(holding.Industry, holding.Sector);
                return new RiskAssetMetadata(id, holding.Ticker, holding.Exchange, holding.SecurityName, industry,
                    IndustryClassification.Raw(holding.Industry, holding.Sector));
            }).ToArray()));
    }

    public async Task<Result<PortfolioMonteCarloPreparedData>> PrepareMonteCarloAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        static PortfolioMonteCarloPreparedData Unavailable(string message, DateOnly? asOf = null, int commonDays = 0) =>
            new(null, message, asOf, commonDays);

        var portfolio = await portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null)
            return Result<PortfolioMonteCarloPreparedData>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0)
            return Result<PortfolioMonteCarloPreparedData>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");

        var securityIds = portfolio.Holdings.Select(holding => holding.SecurityId).Distinct().ToList();
        var latestPrices = await marketPriceRepository.GetLatestPricesAsync(securityIds, DailyInterval, cancellationToken);
        if (latestPrices.Count != securityIds.Count)
            return Result<PortfolioMonteCarloPreparedData>.Success(Unavailable("所有持倉都需要最新有效價格，才能模擬共同投組路徑。"));

        var valuations = await Task.WhenAll(portfolio.Holdings.Select(async holding =>
        {
            var marketValue = PortfolioMath.CalculateMarketValue(holding.Quantity, latestPrices[holding.SecurityId].Price);
            var baseValue = await exchangeRateService.ConvertAsync(
                marketValue, holding.CostCurrency, portfolio.BaseCurrency, cancellationToken);
            return (holding.SecurityId, baseValue);
        }));
        var totalValue = valuations.Sum(value => value.baseValue);
        if (totalValue <= 0)
            return Result<PortfolioMonteCarloPreparedData>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = valuations.ToDictionary(value => value.SecurityId, value => value.baseValue / totalValue);

        var pricesBySecurity = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var securityId in securityIds)
            pricesBySecurity[securityId] = await marketPriceRepository.GetBySecurityAsync(securityId, null, null, cancellationToken);
        if (pricesBySecurity.Values.Any(series => series.Count < MinPriceCount))
            return Result<PortfolioMonteCarloPreparedData>.Success(Unavailable($"所有持倉至少需要 {MinPriceCount} 筆價格資料，才能建立共同日報酬。"));

        var commonDates = pricesBySecurity.Values
            .Select(series => series.Select(price => DateOnly.FromDateTime(price.PriceTime)).ToHashSet())
            .Aggregate((left, right) => { left.IntersectWith(right); return left; })
            .OrderBy(date => date)
            .ToList();
        var dataAsOf = commonDates.LastOrDefault();
        if (commonDates.Count < MinPriceCount)
            return Result<PortfolioMonteCarloPreparedData>.Success(Unavailable(
                $"可用的共同日價格只有 {commonDates.Count} 筆，至少需要 {MinPriceCount} 筆。", dataAsOf, commonDates.Count));

        var alignedPrices = pricesBySecurity.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<decimal>)pair.Value
                .Where(price => commonDates.Contains(DateOnly.FromDateTime(price.PriceTime)))
                .OrderBy(price => price.PriceTime)
                .Select(price => price.AdjustedClose ?? price.Close)
                .ToList());
        if (alignedPrices.Values.Any(series => series.Any(price => price <= 0)))
            return Result<PortfolioMonteCarloPreparedData>.Failure("risk.non_positive_price", "Historical prices contain non-positive values.");

        return Result<PortfolioMonteCarloPreparedData>.Success(new(
            new PortfolioSimulationData(alignedPrices, weights, totalValue), null, dataAsOf, commonDates.Count));
    }
}
