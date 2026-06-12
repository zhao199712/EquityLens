using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.ExchangeRates;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>
/// 風險分析服務實作，提供個股與投資組合的風險指標計算。
/// </summary>
public sealed class RiskAnalysisService : IRiskAnalysisService
{
    private const string DailyInterval = "1d";
    private const int MinPriceCount = 30;
    private const decimal MaxConfidenceLevel = 0.999m;
    private const decimal MinConfidenceLevel = 0.90m;
    private const int MaxSimulations = 100000;
    private const int MinSimulations = 1000;
    private const decimal EwmaLambda = 0.94m;
    private const string VolatilityMethod = "EWMA";
    private const string DriftAssumption = "ZeroDrift";
    private static readonly int[] SupportedHorizons = [1, 7, 30];

    private readonly ISecurityRepository _securityRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IExchangeRateService _exchangeRateService;

    /// <summary>
    /// 初始化風險分析服務。
    /// </summary>
    /// <param name="securityRepository">證券儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    /// <param name="exchangeRateService">匯率服務。</param>
    public RiskAnalysisService(
        ISecurityRepository securityRepository,
        IMarketPriceRepository marketPriceRepository,
        IPortfolioRepository portfolioRepository,
        IExchangeRateService exchangeRateService)
    {
        _securityRepository = securityRepository;
        _marketPriceRepository = marketPriceRepository;
        _portfolioRepository = portfolioRepository;
        _exchangeRateService = exchangeRateService;
    }

    /// <inheritdoc />
    public async Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
        Guid securityId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        CancellationToken cancellationToken)
    {
        if (!await _securityRepository.ActiveExistsAsync(securityId, cancellationToken))
        {
            return Result<SecurityRiskResponse>.Failure("security.not_found", "Security was not found.");
        }

        if (from >= to)
        {
            return Result<SecurityRiskResponse>.Failure(
                "risk.invalid_date_range", "'from' must be earlier than 'to'.");
        }

        if (confidenceLevel < MinConfidenceLevel || confidenceLevel > MaxConfidenceLevel)
        {
            return Result<SecurityRiskResponse>.Failure(
                "risk.invalid_confidence_level", "Confidence level must be between 0.90 and 0.999.");
        }

        if (simulations < MinSimulations || simulations > MaxSimulations)
        {
            return Result<SecurityRiskResponse>.Failure(
                "risk.invalid_simulations", "Simulations must be between 1000 and 100000.");
        }

        var prices = await _marketPriceRepository.GetBySecurityAsync(
            securityId, from, to, cancellationToken);

        if (prices.Count < MinPriceCount)
        {
            return Result<SecurityRiskResponse>.Failure(
                "risk.insufficient_prices",
                $"At least {MinPriceCount} price records are required for risk estimation (got {prices.Count}).");
        }

        var priceValues = prices
            .Select(x => x.AdjustedClose ?? x.Close)
            .ToList();

        var gbmParams = RiskMath.EstimateGbmParameters(priceValues);

        if (gbmParams is null)
        {
            return Result<SecurityRiskResponse>.Failure(
                "risk.non_positive_price",
                "Historical prices contain non-positive values, cannot estimate GBM parameters.");
        }

        var logReturns = new List<decimal>(priceValues.Count - 1);
        for (var i = 1; i < priceValues.Count; i++)
        {
            var logReturn = (decimal)Math.Log((double)(priceValues[i] / priceValues[i - 1]));
            logReturns.Add(logReturn);
        }

        var ewmaDailyVolatility = RiskMath.CalculateEwmaVolatility(logReturns, EwmaLambda);
        var ewmaAnnualizedVolatility = RiskMath.CalculateAnnualizedVolatility(ewmaDailyVolatility);
        var horizons = BuildSecurityHorizons(
            priceValues[^1], logReturns, ewmaAnnualizedVolatility, simulations, confidenceLevel);

        var response = new SecurityRiskResponse(
            securityId,
            from,
            to,
            gbmParams.PriceCount,
            gbmParams.ReturnCount,
            0,
            ewmaAnnualizedVolatility,
            confidenceLevel,
            simulations,
            VolatilityMethod,
            EwmaLambda,
            DriftAssumption,
            SupportedHorizons,
            horizons);

        return Result<SecurityRiskResponse>.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        Guid providerUserId,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.invalid_date_range", "'from' must be earlier than 'to'.");
        }

        if (confidenceLevel < MinConfidenceLevel || confidenceLevel > MaxConfidenceLevel)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.invalid_confidence_level", "Confidence level must be between 0.90 and 0.999.");
        }

        if (simulations < MinSimulations || simulations > MaxSimulations)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.invalid_simulations", "Simulations must be between 1000 and 100000.");
        }

        var portfolio = await _portfolioRepository.GetDetailAsync(
            portfolioId, providerUserId, cancellationToken);

        if (portfolio is null)
        {
            return Result<PortfolioRiskResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        if (portfolio.Holdings.Count == 0)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "portfolio.no_holdings", "Portfolio has no holdings.");
        }

        var securityIds = portfolio.Holdings
            .Select(x => x.SecurityId)
            .Distinct()
            .ToList();

        var latestPrices = await _marketPriceRepository.GetLatestPricesAsync(
            securityIds, DailyInterval, cancellationToken);

        if (latestPrices.Count == 0)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.insufficient_prices", "No latest prices available for any holding.");
        }

        var pricedHoldings = portfolio.Holdings
            .Where(h => latestPrices.ContainsKey(h.SecurityId))
            .ToList();

        if (pricedHoldings.Count == 0)
        {
            return Result<PortfolioRiskResponse>.Failure(
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
            var marketValueInBase = await _exchangeRateService.ConvertAsync(
                localMarketValue, h.CostCurrency, portfolio.BaseCurrency, cancellationToken);
            return (Holding: h, MarketValueInBase: marketValueInBase);
        });

        var marketValues = await Task.WhenAll(marketValueTasks);
        var totalMarketValue = marketValues.Sum(x => x.MarketValueInBase);

        if (totalMarketValue <= 0)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.invalid_market_value", "Total market value is zero or negative.");
        }

        // 取得各證券歷史價格
        var pricesBySecurity = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var securityId in pricedSecurityIds)
        {
            var prices = await _marketPriceRepository.GetBySecurityAsync(
                securityId, from, to, cancellationToken);
            if (prices.Count >= MinPriceCount)
            {
                pricesBySecurity[securityId] = prices;
            }
        }

        if (pricesBySecurity.Count == 0)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.insufficient_prices",
                $"At least {MinPriceCount} price records are required for risk estimation.");
        }

        // 日期對齊：只保留所有資產共同的交易日
        var dateSets = pricesBySecurity.Values
            .Select(p => p.Select(x => DateOnly.FromDateTime(x.PriceTime)).ToHashSet())
            .ToList();

        var commonDates = dateSets.Skip(1)
            .Aggregate(dateSets[0], (intersection, set) =>
            {
                intersection.IntersectWith(set);
                return intersection;
            })
            .OrderBy(d => d)
            .ToList();

        if (commonDates.Count < MinPriceCount)
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.insufficient_prices",
                $"After date alignment, only {commonDates.Count} common trading days remain " +
                $"(need at least {MinPriceCount}).");
        }

        var dateToPrice = pricesBySecurity.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<decimal>)kvp.Value
                .Where(p => commonDates.Contains(DateOnly.FromDateTime(p.PriceTime)))
                .OrderBy(p => p.PriceTime)
                .Select(p => p.AdjustedClose ?? p.Close)
                .ToList());

        if (dateToPrice.Values.Any(prices => prices.Any(price => price <= 0)))
        {
            return Result<PortfolioRiskResponse>.Failure(
                "risk.non_positive_price",
                "Historical prices contain non-positive values, cannot estimate portfolio risk.");
        }

        // 計算各資產權重
        var weightsBySecurityId = marketValues.ToDictionary(
            x => x.Holding.SecurityId,
            x => x.MarketValueInBase / totalMarketValue);

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
            return Result<PortfolioRiskResponse>.Failure(
                "risk.insufficient_prices", "Not enough aligned return data points.");
        }

        // 計算風險指標
        var volatility = RiskMath.CalculateEwmaVolatility(portfolioReturns, EwmaLambda);
        var annualizedVolatility = RiskMath.CalculateAnnualizedVolatility(volatility);

        // Sharpe 使用 portfolioReturns 的平均值年化
        var meanReturn = portfolioReturns.Average();
        var annualizedReturn = meanReturn * 252;
        var sharpeRatio = RiskMath.CalculateSharpeRatio(annualizedReturn, 0, annualizedVolatility);

        // 計算 portfolio value 序列來算 max drawdown
        var portfolioValues = new List<decimal>(commonDates.Count);
        portfolioValues.Add(totalMarketValue);
        for (var d = 0; d < portfolioReturns.Count; d++)
        {
            var nextValue = portfolioValues[^1] * (1 + portfolioReturns[d]);
            portfolioValues.Add(nextValue);
        }
        var maxDrawdown = RiskMath.CalculateMaxDrawdown(portfolioValues);

        // 各持倉風險摘要
        var holdingRisks = new List<PortfolioHoldingRiskResponse>();
        foreach (var m in marketValues)
        {
            var secPrices = dateToPrice.GetValueOrDefault(m.Holding.SecurityId);
            var secVol = secPrices is not null
                ? CalculateLogReturnVolatility(secPrices)
                : 0m;
            var secAnnVol = RiskMath.CalculateAnnualizedVolatility(secVol);
            var weight = weightsBySecurityId.GetValueOrDefault(m.Holding.SecurityId, 0);

            holdingRisks.Add(new PortfolioHoldingRiskResponse(
                m.Holding.SecurityId,
                m.Holding.Ticker,
                m.Holding.Exchange,
                m.Holding.SecurityName,
                weight,
                secAnnVol,
                secPrices?.Count ?? 0));
        }

        // Correlated zero-drift GBM Monte Carlo for the fixed product horizons.
        var horizons = BuildPortfolioHorizons(
            portfolioReturns, dateToPrice, weightsBySecurityId, totalMarketValue,
            simulations, confidenceLevel);

        var response = new PortfolioRiskResponse(
            portfolioId,
            from,
            to,
            portfolio.BaseCurrency,
            portfolio.Holdings.Count,
            pricedHoldings.Count,
            portfolioReturns.Count,
            totalMarketValue,
            annualizedVolatility,
            maxDrawdown,
            sharpeRatio,
            confidenceLevel,
            simulations,
            VolatilityMethod,
            EwmaLambda,
            DriftAssumption,
            SupportedHorizons,
            horizons,
            holdingRisks);

        return Result<PortfolioRiskResponse>.Success(response);
    }

    private MonteCarloResult RunCorrelatedMonteCarlo(
        IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> alignedPrices,
        IReadOnlyDictionary<Guid, decimal> weights,
        decimal totalMarketValue,
        int horizonDays,
        int simulations,
        decimal confidenceLevel)
    {
        var assetIds = alignedPrices.Keys
            .Where(id => weights.GetValueOrDefault(id, 0) > 0)
            .ToList();
        var n = assetIds.Count;
        if (n == 0)
        {
            return new MonteCarloResult(0, 0, 0, 0, 0, 0, confidenceLevel);
        }

        // Compute log returns matrix and EWMA volatility for each asset.
        var returnsMatrix = new List<IReadOnlyList<decimal>>(n);
        var initialValues = new List<decimal>(n);
        var drifts = new List<decimal>(n);
        var vols = new List<decimal>(n);
        var weightList = new List<decimal>(n);

        foreach (var id in assetIds)
        {
            var prices = alignedPrices[id];
            initialValues.Add(prices[^1]);
            weightList.Add(weights.GetValueOrDefault(id, 0));

            var logReturns = new List<decimal>(prices.Count - 1);
            for (var i = 1; i < prices.Count; i++)
            {
                if (prices[i - 1] <= 0 || prices[i] <= 0)
                {
                    logReturns.Clear();
                    break;
                }
                logReturns.Add((decimal)Math.Log((double)(prices[i] / prices[i - 1])));
            }

            if (logReturns.Count < 2)
            {
                returnsMatrix.Add(Array.Empty<decimal>());
                drifts.Add(0);
                vols.Add(0);
                continue;
            }

            returnsMatrix.Add(logReturns);
            var dailyVol = RiskMath.CalculateEwmaVolatility(logReturns, EwmaLambda);
            var annVol = RiskMath.CalculateAnnualizedVolatility(dailyVol);
            drifts.Add(0);
            vols.Add(annVol);
        }

        // Only include assets with valid returns
        var validIndices = Enumerable.Range(0, n)
            .Where(i => returnsMatrix[i].Count >= 2)
            .ToList();

        if (validIndices.Count < 2)
        {
            if (validIndices.Count == 1)
            {
                var i = validIndices[0];
                return RiskMath.RunMonteCarloSimulation(
                    totalMarketValue,
                    drifts[i],
                    vols[i],
                    horizonDays,
                    simulations,
                    confidenceLevel);
            }

            return new MonteCarloResult(0, 0, 0, 0, 0, 0, confidenceLevel);
        }

        var filteredReturns = validIndices.Select(i => returnsMatrix[i]).ToList();
        var correlationMatrix = RiskMath.CalculateCorrelationMatrix(filteredReturns);

        var filteredInitial = validIndices.Select(i => initialValues[i]).ToList();
        var filteredDrifts = validIndices.Select(i => drifts[i]).ToList();
        var filteredVols = validIndices.Select(i => vols[i]).ToList();
        var filteredWeights = validIndices.Select(i => weightList[i]).ToList();

        var weightSum = filteredWeights.Sum();
        if (weightSum <= 0)
        {
            return new MonteCarloResult(0, 0, 0, 0, 0, 0, confidenceLevel);
        }

        var normalizedWeights = filteredWeights
            .Select(weight => weight / weightSum)
            .ToList();

        var mcResult = RiskMath.RunCorrelatedGbmMonteCarloSimulation(
            filteredInitial, filteredDrifts, filteredVols, filteredWeights,
            correlationMatrix, horizonDays, simulations, confidenceLevel,
            initialPortfolioValue: totalMarketValue,
            portfolioWeights: normalizedWeights);

        return mcResult;
    }

    private static IReadOnlyList<RiskHorizonResult> BuildSecurityHorizons(
        decimal initialValue,
        IReadOnlyList<decimal> logReturns,
        decimal annualizedVolatility,
        int simulations,
        decimal confidenceLevel)
    {
        return SupportedHorizons
            .Select(horizonDays =>
            {
                var rollingReturns = RiskMath.CalculateRollingLogReturns(logReturns, horizonDays);
                var mcResult = RiskMath.RunMonteCarloSimulation(
                    initialValue,
                    0,
                    annualizedVolatility,
                    horizonDays,
                    simulations,
                    confidenceLevel);

                return new RiskHorizonResult(
                    horizonDays,
                    RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel),
                    RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel),
                    mcResult.SimulatedVaR,
                    mcResult.SimulatedES,
                    mcResult.MeanFinalValue,
                    mcResult.MedianFinalValue,
                    mcResult.WorstCase95Percentile,
                    mcResult.BestCase95Percentile);
            })
            .ToList();
    }

    private IReadOnlyList<RiskHorizonResult> BuildPortfolioHorizons(
        IReadOnlyList<decimal> portfolioReturns,
        IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> alignedPrices,
        IReadOnlyDictionary<Guid, decimal> weights,
        decimal totalMarketValue,
        int simulations,
        decimal confidenceLevel)
    {
        return SupportedHorizons
            .Select(horizonDays =>
            {
                var rollingReturns = RiskMath.CalculateRollingLogReturns(portfolioReturns, horizonDays);
                var mcResult = RunCorrelatedMonteCarlo(
                    alignedPrices,
                    weights,
                    totalMarketValue,
                    horizonDays,
                    simulations,
                    confidenceLevel);

                return new RiskHorizonResult(
                    horizonDays,
                    RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel),
                    RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel),
                    mcResult.SimulatedVaR,
                    mcResult.SimulatedES,
                    mcResult.MeanFinalValue,
                    mcResult.MedianFinalValue,
                    mcResult.WorstCase95Percentile,
                    mcResult.BestCase95Percentile);
            })
            .ToList();
    }

    private static decimal CalculateLogReturnVolatility(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 2) return 0;
        var logReturns = new List<decimal>(prices.Count - 1);
        for (var i = 1; i < prices.Count; i++)
        {
            if (prices[i - 1] <= 0 || prices[i] <= 0) return 0;
            logReturns.Add((decimal)Math.Log((double)(prices[i] / prices[i - 1])));
        }
        return RiskMath.CalculateEwmaVolatility(logReturns, EwmaLambda);
    }
}
