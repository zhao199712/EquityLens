using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>
/// 風險分析服務實作，提供個股與投資組合的風險指標計算。
/// </summary>
public sealed class RiskAnalysisService : IRiskAnalysisService, IRiskBacktestInputProvider
{
    private const string DailyInterval = "1d";
    // 最低共同交易日門檻(約一個月),讓 1M 以上區間可估算風險;所有持倉必須在相同日期具有有效價格。
    private const int MinPriceCount = 20;
    private const decimal MaxConfidenceLevel = 0.999m;
    private const decimal MinConfidenceLevel = 0.90m;
    private const int MaxSimulations = 100000;
    private const int MinSimulations = 1000;
    private const decimal EwmaLambda = 0.94m;
    private const string MvewmaFhsModel = "mvewma_fhs";
    private const string ConservativeMvewmaFhsModel = "mvewma_fhs_conservative";
    private const decimal ConservativeResidualCapQuantile = 0.99m;
    private const string VolatilityMethod = "EWMA";
    private const string DriftAssumption = "ZeroDrift";
    private static readonly int[] SupportedHorizons = [1, 7, 30];

    private readonly ISecurityRepository _securityRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly EquityLensDbContext? _dbContext;
    private readonly ILogger<RiskAnalysisService>? _logger;
    private readonly IRiskBacktestEngine _riskBacktestEngine;

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
        IExchangeRateService exchangeRateService,
        EquityLensDbContext? dbContext = null,
        ILogger<RiskAnalysisService>? logger = null,
        IRiskBacktestEngine? riskBacktestEngine = null)
    {
        _securityRepository = securityRepository;
        _marketPriceRepository = marketPriceRepository;
        _portfolioRepository = portfolioRepository;
        _exchangeRateService = exchangeRateService;
        _dbContext = dbContext;
        _logger = logger;
        _riskBacktestEngine = riskBacktestEngine ?? new CSharpRiskBacktestEngine();
    }

    private async Task<Result<T>> ExecuteRiskOperationAsync<T>(
        string operation,
        string? model,
        decimal? confidenceLevel,
        int? simulations,
        Func<Task<Result<T>>> execute)
    {
        using var activity = EquityLensTelemetry.StartRiskOperation(operation, model, confidenceLevel, simulations);
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogInformation("Risk operation {RiskOperation} started. Model={RiskModel} Confidence={RiskConfidence} Simulations={RiskSimulations}",
            operation, model, confidenceLevel, simulations);

        try
        {
            var result = await execute();
            stopwatch.Stop();
            EquityLensTelemetry.CompleteRiskOperation(activity, operation, stopwatch, result.IsSuccess, result.ErrorCode, model, confidenceLevel);
            _logger?.LogInformation("Risk operation {RiskOperation} completed. Outcome={RiskOutcome} DurationMs={RiskDurationMs} ErrorCode={RiskErrorCode}",
                operation, result.IsSuccess ? "success" : "failure", stopwatch.ElapsedMilliseconds, result.ErrorCode);
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            EquityLensTelemetry.MarkError(activity, exception);
            EquityLensTelemetry.CompleteRiskOperation(activity, operation, stopwatch, false, exception.GetType().Name, model, confidenceLevel);
            _logger?.LogError(exception, "Risk operation {RiskOperation} failed after {RiskDurationMs}ms", operation, stopwatch.ElapsedMilliseconds);
            throw;
        }
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
        CancellationToken cancellationToken,
        string modelName = "gbm_ewma_normal",
        IReadOnlyDictionary<Guid, decimal>? targetWeights = null) =>
        await ExecuteRiskOperationAsync(
            "risk.portfolio.calculate", modelName, confidenceLevel, simulations,
            () => GetPortfolioRiskCoreAsync(portfolioId, from, to, horizonDays, confidenceLevel, simulations,
                providerUserId, cancellationToken, modelName, targetWeights));

    private async Task<Result<PortfolioRiskResponse>> GetPortfolioRiskCoreAsync(
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

        if (modelName is not ("gbm_ewma_normal" or MvewmaFhsModel or ConservativeMvewmaFhsModel))
            return Result<PortfolioRiskResponse>.Failure("risk.invalid_model", "Unsupported risk model.");

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

        IReadOnlyDictionary<Guid, LatestMarketPrice> latestPrices;
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "prices.load"))
        {
            latestPrices = await _marketPriceRepository.GetLatestPricesAsync(
                securityIds, DailyInterval, cancellationToken);
        }

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
        var priceCountsBySecurity = new Dictionary<Guid, int>();
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "prices.load"))
        {
            foreach (var securityId in pricedSecurityIds)
            {
                var prices = await _marketPriceRepository.GetBySecurityAsync(
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
            return Result<PortfolioRiskResponse>.Failure(
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
            return Result<PortfolioRiskResponse>.Failure(
                "risk.insufficient_prices",
                $"After date alignment, only {commonDates.Count} common trading days remain " +
                $"(need at least {MinPriceCount}).");
        }

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
            var nextValue = portfolioValues[^1] * (decimal)Math.Exp((double)portfolioReturns[d]);
            portfolioValues.Add(nextValue);
        }
        var maxDrawdown = RiskMath.CalculateMaxDrawdown(portfolioValues);
        var classificationsBySecurityId = marketValues.ToDictionary(
            value => value.Holding.SecurityId,
            value => ResolveIndustry(value.Holding));
        IReadOnlyDictionary<Guid, VolatilityRiskContribution> riskSourcesBySecurityId;
        IReadOnlyList<PortfolioIndustryRiskResponse> industryRiskSources;
        decimal riskSourceAnnualizedVolatility;
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "risk-sources"))
        {
            (riskSourcesBySecurityId, industryRiskSources, riskSourceAnnualizedVolatility) = BuildRiskSources(
                dateToPrice, weightsBySecurityId, classificationsBySecurityId, modelName);
        }

        // 各持倉風險來源
        var holdingRisks = new List<PortfolioHoldingRiskResponse>();
        foreach (var m in marketValues)
        {
            var secPrices = dateToPrice.GetValueOrDefault(m.Holding.SecurityId);
            var secVol = secPrices is not null
                ? CalculateLogReturnVolatility(secPrices)
                : 0m;
            var secAnnVol = RiskMath.CalculateAnnualizedVolatility(secVol);
            var weight = weightsBySecurityId.GetValueOrDefault(m.Holding.SecurityId, 0);
            var source = riskSourcesBySecurityId.GetValueOrDefault(
                m.Holding.SecurityId, VolatilityRiskContribution.Zero);

            holdingRisks.Add(new PortfolioHoldingRiskResponse(
                m.Holding.SecurityId,
                m.Holding.Ticker,
                m.Holding.Exchange,
                m.Holding.SecurityName,
                weight,
                secAnnVol,
                secPrices?.Count ?? 0,
                classificationsBySecurityId[m.Holding.SecurityId],
                source.ComponentVolatility,
                source.ComponentRiskShare,
                source.MarginalVolatility,
                source.IncrementalVolatility));
        }

        var (concentrationHhi, largestHoldingWeight) = RiskMath.CalculateConcentration(
            weightsBySecurityId.Values.ToList());
        var dataAsOfDate = commonDates[^1];

        // Correlated zero-drift GBM Monte Carlo for the fixed product horizons.
        IReadOnlyList<RiskHorizonResult> horizons;
        string? covarianceMethod = null;
        string? residualSampling = null;
        int? commonTradingDays = null;
        decimal? shrinkageAlpha = null;
        using (EquityLensTelemetry.StartRiskStage(Activity.Current, "horizons"))
        {
            if (modelName is MvewmaFhsModel or ConservativeMvewmaFhsModel)
            {
                horizons = BuildPortfolioHorizonsMvewmaFhs(
                    portfolioReturns, dateToPrice, weightsBySecurityId, totalMarketValue,
                    simulations, confidenceLevel,
                    modelName == ConservativeMvewmaFhsModel ? ConservativeResidualCapQuantile : 0m,
                    out var alpha, out var ctd);
                shrinkageAlpha = alpha;
                commonTradingDays = ctd;
                covarianceMethod = "MultivariateEWMA";
                residualSampling = "HistoricalVectorBootstrap";
            }
            else
            {
                horizons = BuildPortfolioHorizons(
                    portfolioReturns, dateToPrice, weightsBySecurityId, totalMarketValue,
                    simulations, confidenceLevel);
            }
        }

        Activity.Current?.SetTag("risk.holdings_count", portfolio.Holdings.Count);
        Activity.Current?.SetTag("risk.priced_holdings_count", pricedHoldings.Count);
        Activity.Current?.SetTag("risk.common_trading_days", commonDates.Count);

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
            holdingRisks,
            CovarianceMethod: covarianceMethod,
            ResidualSampling: residualSampling,
            CommonTradingDays: commonTradingDays,
            ShrinkageAlpha: shrinkageAlpha,
            DataAsOfDate: dataAsOfDate,
            ConcentrationHhi: concentrationHhi,
            LargestHoldingWeight: largestHoldingWeight,
            Industries: industryRiskSources,
            RiskSourceAnnualizedVolatility: riskSourceAnnualizedVolatility,
            DailyLogReturns: portfolioReturns);
        return Result<PortfolioRiskResponse>.Success(response);
    }

    public async Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken) =>
        await ExecuteRiskOperationAsync(
            "risk.portfolio.backtest", "mvewma_fhs", null, 5000,
            async () =>
            {
                var prepared = await PreparePortfolioRiskBacktestInputAsync(
                    portfolioId, from, to, providerUserId, cancellationToken);
                if (!prepared.IsSuccess)
                    return Result<PortfolioRiskBacktestResponse>.Failure(
                        prepared.ErrorCode!, prepared.ErrorMessage!);
                return _riskBacktestEngine.Calculate(prepared.Value!, cancellationToken).ToResult();
            });

    public async Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken)
    {
        const int lookbackDays = 252;
        const int minimumObservations = 100;
        const int simulations = 5000;
        if (from >= to) return Result<RiskBacktestEngineInput>.Failure("risk.invalid_date_range", "'from' must be earlier than 'to'.");

        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<RiskBacktestEngineInput>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<RiskBacktestEngineInput>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");

        var ids = portfolio.Holdings.Select(h => h.SecurityId).Distinct().ToList();
        var latest = await _marketPriceRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        var holdings = portfolio.Holdings.Where(h => latest.ContainsKey(h.SecurityId)).ToList();
        if (holdings.Count == 0) return Result<RiskBacktestEngineInput>.Failure("risk.insufficient_prices", "No latest prices available for portfolio holdings.");

        var values = await Task.WhenAll(holdings.Select(async h =>
        {
            var localValue = PortfolioMath.CalculateMarketValue(h.Quantity, latest[h.SecurityId].Price);
            return (h.SecurityId, Value: await _exchangeRateService.ConvertAsync(localValue, h.CostCurrency, portfolio.BaseCurrency, cancellationToken));
        }));
        var total = values.Sum(v => v.Value);
        if (total <= 0) return Result<RiskBacktestEngineInput>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = values.ToDictionary(v => v.SecurityId, v => v.Value / total);

        var pricesById = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var id in weights.Keys)
            pricesById[id] = await _marketPriceRepository.GetBySecurityAsync(id, from, to, cancellationToken);
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
            weightList));
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

    private static string ResolveIndustry(EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse holding) =>
        string.IsNullOrWhiteSpace(holding.Industry)
            ? string.IsNullOrWhiteSpace(holding.Sector) ? "未分類" : holding.Sector
            : holding.Industry;

    private static (
        IReadOnlyDictionary<Guid, VolatilityRiskContribution> BySecurityId,
        IReadOnlyList<PortfolioIndustryRiskResponse> Industries,
        decimal AnnualizedVolatility)
        BuildRiskSources(
            IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> alignedPrices,
            IReadOnlyDictionary<Guid, decimal> weightsBySecurityId,
            IReadOnlyDictionary<Guid, string> classificationsBySecurityId,
            string modelName)
    {
        var assetIds = alignedPrices.Keys
            .Where(id => weightsBySecurityId.GetValueOrDefault(id, 0) > 0)
            .ToList();
        if (assetIds.Count == 0)
            return (new Dictionary<Guid, VolatilityRiskContribution>(), Array.Empty<PortfolioIndustryRiskResponse>(), 0);

        var returnMatrix = assetIds.Select(id =>
        {
            var prices = alignedPrices[id];
            return (IReadOnlyList<decimal>)Enumerable.Range(1, prices.Count - 1)
                .Select(index => (decimal)Math.Log((double)(prices[index] / prices[index - 1])))
                .ToList();
        }).ToList();
        var weights = assetIds.Select(id => weightsBySecurityId[id]).ToList();
        var covarianceSeries = RiskMath.CalculateMultivariateEwmaCovariances(returnMatrix, EwmaLambda);
        if (covarianceSeries.Count == 0)
            return (new Dictionary<Guid, VolatilityRiskContribution>(), Array.Empty<PortfolioIndustryRiskResponse>(), 0);

        if (modelName is MvewmaFhsModel or ConservativeMvewmaFhsModel)
        {
            var alpha = RiskMath.DetermineAutoShrinkageAlpha(assetIds.Count, returnMatrix[0].Count);
            covarianceSeries = RiskMath.AddJitter(RiskMath.ApplyDiagonalShrinkage(covarianceSeries, alpha));
        }

        var covariance = covarianceSeries[^1];
        var totalRiskSource = RiskMath.CalculateVolatilityRiskContribution(
            weights, covariance, Enumerable.Range(0, assetIds.Count).ToList());
        var bySecurityId = assetIds
            .Select((id, index) => new
            {
                Id = id,
                Source = RiskMath.CalculateVolatilityRiskContribution(weights, covariance, [index]),
            })
            .ToDictionary(value => value.Id, value => value.Source);

        var industries = assetIds
            .Select((id, index) => new { Id = id, Index = index, Industry = classificationsBySecurityId.GetValueOrDefault(id, "未分類") })
            .GroupBy(value => value.Industry)
            .Select(group =>
            {
                var indices = group.Select(value => value.Index).ToList();
                var source = RiskMath.CalculateVolatilityRiskContribution(weights, covariance, indices);
                return new PortfolioIndustryRiskResponse(
                    group.Key,
                    indices.Sum(index => weights[index]),
                    source.ComponentVolatility,
                    source.ComponentRiskShare,
                    source.MarginalVolatility,
                    source.IncrementalVolatility,
                    indices.Count);
            })
            .OrderByDescending(source => source.ComponentRiskShare)
            .ToList();

        return (bySecurityId, industries, totalRiskSource.ComponentVolatility);
    }

    private IReadOnlyList<RiskHorizonResult> BuildPortfolioHorizonsMvewmaFhs(
        IReadOnlyList<decimal> portfolioReturns,
        IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> alignedPrices,
        IReadOnlyDictionary<Guid, decimal> weights,
        decimal totalMarketValue,
        int simulations,
        decimal confidenceLevel,
        decimal residualCapQuantile,
        out decimal shrinkageAlpha,
        out int commonTradingDays)
    {
        var (returnMatrix, weightList) = BuildReturnMatrix(alignedPrices, weights);
        var assetCount = returnMatrix.Count;
        commonTradingDays = assetCount > 0 ? returnMatrix[0].Count : 0;
        shrinkageAlpha = RiskMath.DetermineAutoShrinkageAlpha(assetCount, commonTradingDays);

        var alpha = shrinkageAlpha;
        var results = new List<RiskHorizonResult>(SupportedHorizons.Length);
        foreach (var horizonDays in SupportedHorizons)
        {
            var rollingReturns = RiskMath.CalculateRollingLogReturns(portfolioReturns, horizonDays);
            var fhsResult = RiskMath.RunMultivariateFhsSimulation(
                returnMatrix, weightList, totalMarketValue, horizonDays,
                simulations, confidenceLevel, EwmaLambda, alpha, residualCapQuantile: residualCapQuantile);

            results.Add(new RiskHorizonResult(
                horizonDays,
                RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel),
                RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel),
                fhsResult.SimulatedVaR,
                fhsResult.SimulatedES,
                fhsResult.MeanFinalValue,
                fhsResult.MedianFinalValue,
                fhsResult.WorstCase95Percentile,
                fhsResult.BestCase95Percentile));
        }
        return results;
    }

    private static (IReadOnlyList<IReadOnlyList<decimal>> ReturnMatrix, IReadOnlyList<decimal> Weights) BuildReturnMatrix(
        IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> alignedPrices,
        IReadOnlyDictionary<Guid, decimal> weights)
    {
        var assetIds = alignedPrices.Keys
            .Where(id => weights.GetValueOrDefault(id, 0) > 0)
            .ToList();

        var returnMatrix = new List<IReadOnlyList<decimal>>();
        var weightList = new List<decimal>();

        foreach (var id in assetIds)
        {
            var prices = alignedPrices[id];
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

            if (logReturns.Count < 2) continue;

            returnMatrix.Add(logReturns);
            weightList.Add(weights.GetValueOrDefault(id, 0));
        }

        return (returnMatrix, weightList);
    }

    public async Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken, string modelName = MvewmaFhsModel) =>
        await ExecuteRiskOperationAsync(
            "risk.portfolio.monte-carlo", modelName, null, 10000,
            () => GetPortfolioMonteCarloCoreAsync(portfolioId, providerUserId, cancellationToken, modelName));

    private async Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloCoreAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken, string modelName = MvewmaFhsModel)
    {
        const int horizonDays = 252;
        const int simulations = 10000;
        const int samplePathCount = 8;
        if (modelName is not (MvewmaFhsModel or ConservativeMvewmaFhsModel))
            return Result<PortfolioMonteCarloResponse>.Failure("risk.invalid_model", "Unsupported Monte Carlo model.");
        var residualCapQuantile = modelName == ConservativeMvewmaFhsModel ? ConservativeResidualCapQuantile : 0m;
        var displayModel = residualCapQuantile > 0 ? "MVEWMA-FHS（保守 p99）" : "MVEWMA-FHS";
        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null)
            return Result<PortfolioMonteCarloResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0)
            return Result<PortfolioMonteCarloResponse>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");

        PortfolioMonteCarloResponse Insufficient(string message, DateOnly? asOf = null, int commonDays = 0) =>
            new(portfolioId, "insufficient_prices", message, asOf, commonDays, horizonDays, simulations,
                displayModel, EwmaLambda, 0, residualCapQuantile, 0, Array.Empty<PortfolioMonteCarloBandPoint>(),
                Array.Empty<PortfolioMonteCarloPath>(), 0, 0, 0, 0,
                new PortfolioMonteCarloDiagnostics(0, 0, 0, 0, 0, 0, 0, residualCapQuantile, 0, false, null));

        var securityIds = portfolio.Holdings.Select(holding => holding.SecurityId).Distinct().ToList();
        var latestPrices = await _marketPriceRepository.GetLatestPricesAsync(securityIds, DailyInterval, cancellationToken);
        if (latestPrices.Count != securityIds.Count)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient("所有持倉都需要最新有效價格，才能模擬共同投組路徑。"));

        var valuations = await Task.WhenAll(portfolio.Holdings.Select(async holding =>
        {
            var marketValue = PortfolioMath.CalculateMarketValue(holding.Quantity, latestPrices[holding.SecurityId].Price);
            var baseValue = await _exchangeRateService.ConvertAsync(
                marketValue, holding.CostCurrency, portfolio.BaseCurrency, cancellationToken);
            return (holding.SecurityId, baseValue);
        }));
        var totalValue = valuations.Sum(value => value.baseValue);
        if (totalValue <= 0)
            return Result<PortfolioMonteCarloResponse>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = valuations.ToDictionary(value => value.SecurityId, value => value.baseValue / totalValue);

        var pricesBySecurity = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var securityId in securityIds)
            pricesBySecurity[securityId] = await _marketPriceRepository.GetBySecurityAsync(securityId, null, null, cancellationToken);
        if (pricesBySecurity.Values.Any(series => series.Count < MinPriceCount))
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient($"所有持倉至少需要 {MinPriceCount} 筆價格資料，才能建立共同日報酬。"));

        var commonDates = pricesBySecurity.Values
            .Select(series => series.Select(price => DateOnly.FromDateTime(price.PriceTime)).ToHashSet())
            .Aggregate((left, right) => { left.IntersectWith(right); return left; })
            .OrderBy(date => date)
            .ToList();
        var dataAsOf = commonDates.LastOrDefault();
        if (commonDates.Count < MinPriceCount)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient(
                $"可用的共同日價格只有 {commonDates.Count} 筆，至少需要 {MinPriceCount} 筆。", dataAsOf, commonDates.Count));

        var alignedPrices = pricesBySecurity.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<decimal>)pair.Value
                .Where(price => commonDates.Contains(DateOnly.FromDateTime(price.PriceTime)))
                .OrderBy(price => price.PriceTime)
                .Select(price => price.AdjustedClose ?? price.Close)
                .ToList());
        if (alignedPrices.Values.Any(series => series.Any(price => price <= 0)))
            return Result<PortfolioMonteCarloResponse>.Failure("risk.non_positive_price", "Historical prices contain non-positive values.");

        var (returnMatrix, weightList) = BuildReturnMatrix(alignedPrices, weights);
        if (returnMatrix.Count != securityIds.Count || returnMatrix.Count == 0)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient("無法建立所有持倉的有效共同日報酬。", dataAsOf, commonDates.Count));
        var shrinkage = RiskMath.DetermineAutoShrinkageAlpha(returnMatrix.Count, returnMatrix[0].Count);
        var seed = BuildMonteCarloSeed(portfolioId, dataAsOf, horizonDays, simulations);
        var simulation = RiskMath.RunMultivariateFhsPathSimulation(
            returnMatrix, weightList, horizonDays, simulations, samplePathCount, seed, EwmaLambda, shrinkage, residualCapQuantile);
        if (simulation.DidFallback)
            return Result<PortfolioMonteCarloResponse>.Success(Insufficient("共同日報酬不足，無法完成 MVEWMA-FHS 模擬。", dataAsOf, commonDates.Count));

        var bands = simulation.Bands.Select(band => new PortfolioMonteCarloBandPoint(
            band.Day, band.P1, band.P5, band.P50, band.P95, band.P99)).ToList();
        var paths = simulation.SamplePaths.Select((path, index) => new PortfolioMonteCarloPath(index + 1, path)).ToList();
        var rightSkewWarning = RiskMath.HasMaterialRightSkew(simulation.ExpectedMedianGap);
        var diagnostics = new PortfolioMonteCarloDiagnostics(
            simulation.AnnualizedPortfolioVolatility,
            simulation.ResidualNormP99,
            simulation.MaxResidualNorm,
            simulation.P50FinalReturn,
            simulation.P95FinalReturn,
            simulation.P99FinalReturn,
            simulation.ExpectedMedianGap,
            simulation.ResidualCapQuantile,
            simulation.CappedDrawRate,
            rightSkewWarning,
            rightSkewWarning
                ? "期望期末報酬明顯高於中位數，表示平均值受到少數高報酬路徑拉高；請優先參考中位數與下行情境。"
                : null);
        return Result<PortfolioMonteCarloResponse>.Success(new(
            portfolioId, "ready", null, dataAsOf, simulation.CommonTradingDays, horizonDays, simulations,
            displayModel, EwmaLambda, simulation.ShrinkageAlpha, simulation.ResidualCapQuantile,
            simulation.CappedDrawRate, bands, paths,
            simulation.PositiveReturnProbability, simulation.ExpectedReturn,
            simulation.P5FinalReturn, simulation.P1FinalReturn, diagnostics));
    }

    private static int BuildMonteCarloSeed(Guid portfolioId, DateOnly dataAsOf, int horizonDays, int simulations)
    {
        var bytes = portfolioId.ToByteArray();
        var seed = 17;
        foreach (var value in bytes) seed = unchecked(seed * 31 + value);
        seed = unchecked(seed * 31 + dataAsOf.DayNumber);
        seed = unchecked(seed * 31 + horizonDays);
        return unchecked(seed * 31 + simulations);
    }

    public async Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        await ExecuteRiskOperationAsync(
            "risk.portfolio.governance", MvewmaFhsModel, .95m, 5000,
            () => GetPortfolioRiskGovernanceCoreAsync(portfolioId, providerUserId, cancellationToken));

    private async Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceCoreAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var risk = await GetPortfolioRiskAsync(portfolioId, to.AddYears(-1), to, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel);
        if (!risk.IsSuccess) return Result<PortfolioRiskGovernanceResponse>.Failure("risk.insufficient_prices", "Risk governance cannot be evaluated because formal risk data is unavailable.");
        return Result<PortfolioRiskGovernanceResponse>.Success(BuildGovernance(portfolioId, risk.Value!, 0m, to));
    }

    private static PortfolioRiskGovernanceResponse BuildGovernance(Guid portfolioId, PortfolioRiskResponse value, decimal cashWeight, DateOnly today)
    {
        var alerts = new List<PortfolioRiskAlertResponse>();
        void Add(string code, decimal current, decimal warning, decimal critical, bool lowerIsWorse, string text)
        {
            var status = lowerIsWorse ? current <= critical ? "critical" : current <= warning ? "warning" : "normal" : current > critical ? "critical" : current > warning ? "warning" : "normal";
            alerts.Add(new(code, status, current, warning, critical, text));
        }
        Add("concentration.largest_holding", value.LargestHoldingWeight, .40m, .50m, false, "降低單一持倉或增加分散配置。");
        Add("concentration.hhi", value.ConcentrationHhi, .25m, .35m, false, "降低集中產業或單一標的曝險。");
        var stale = value.DataAsOfDate is null ? 999m : today.DayNumber - value.DataAsOfDate.Value.DayNumber;
        Add("data.price_age_days", stale, 5m, 10m, false, "請同步最新價格資料後重新計算。");
        var leverage = Math.Max(0, -cashWeight);
        Add("leverage.financing", leverage, .20m, .40m, false, "融資槓桿未納入利率、保證金、追繳與被迫平倉風險。");
        return new(portfolioId, stale > 10 ? "critical" : value.CommonTradingDays < 120 ? "critical" : stale > 5 ? "warning" : "ready", value.DataAsOfDate, value.CommonTradingDays ?? 0, alerts.OrderByDescending(a => a.Status).ToList());
    }

    public async Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshotAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (_dbContext is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var risk = await GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 10000, providerUserId, cancellationToken, MvewmaFhsModel);
        var governance = await GetPortfolioRiskGovernanceAsync(portfolioId, providerUserId, cancellationToken);
        var stress = await GetPortfolioStressTestAsync(portfolioId, providerUserId, cancellationToken);
        var monteCarlo = await GetPortfolioMonteCarloAsync(portfolioId, providerUserId, cancellationToken, MvewmaFhsModel);
        var backtest = await GetPortfolioRiskBacktestAsync(portfolioId, today.AddYears(-3), today, providerUserId, cancellationToken);
        if (!risk.IsSuccess || !governance.IsSuccess || !stress.IsSuccess || !monteCarlo.IsSuccess || !backtest.IsSuccess ||
            monteCarlo.Value!.Status != "ready" || backtest.Value!.Models.Any(model => model.Status != "ready") ||
            stress.Value!.Scenarios.Any(scenario => scenario.Status != "ready"))
        {
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure(
                "risk.snapshot_unavailable", "必要正式風險資料尚不可計算，無法建立不完整的報告快照。");
        }

        var riskValue = risk.Value!;
        var governanceValue = governance.Value!;
        var stressValue = stress.Value!;
        var monteCarloValue = monteCarlo.Value!;
        var backtestValue = backtest.Value!;
        var snapshot = new
        {
            dataQuality = new
            {
                status = governanceValue.DataStatus,
                dataAsOfDate = governanceValue.DataAsOfDate,
                commonTradingDays = governanceValue.CommonTradingDays
            },
            governance = governanceValue.Alerts,
            concentration = new { hhi = riskValue.ConcentrationHhi, largestHoldingWeight = riskValue.LargestHoldingWeight },
            risk = new
            {
                annualizedVolatility = riskValue.HistoricalAnnualizedVolatility,
                maxDrawdown = riskValue.MaxDrawdown,
                sharpeRatio = riskValue.SharpeRatio,
                confidenceLevel = riskValue.ConfidenceLevel,
                horizons = riskValue.Horizons.Select(h => new { h.HorizonDays, h.HistoricalVaR, h.HistoricalES, h.MonteCarloVaR, h.MonteCarloES })
            },
            holdings = riskValue.Holdings.Select(h => new { h.Ticker, h.Exchange, h.SecurityName, h.Industry, h.Weight, h.AnnualizedVolatility, h.ComponentVolatility, h.ComponentRiskShare, h.MarginalVolatility, h.IncrementalVolatility, h.DataPointCount }),
            industries = riskValue.Industries?.Select(i => new { i.Industry, i.HoldingCount, i.Weight, i.ComponentVolatility, i.ComponentRiskShare, i.MarginalVolatility, i.IncrementalVolatility }),
            backtest = backtestValue.Models.Select(m => new { m.Model, m.ConfidenceLevel, m.ObservationCount, m.BreachCount, m.BreachRate, m.ExpectedBreachRate, m.KupiecPValue, m.ChristoffersenPValue, m.TailObservationCount, m.EsTailLossRatio, m.EsStatus, m.Status }),
            stress = stressValue.Scenarios.Select(s => new
            {
                s.Id, s.Name, s.Type, s.Status, s.Methodology, s.From, s.To, s.TotalImpact,
                holdings = s.Holdings.Select(h => new { h.Ticker, h.SecurityName, h.Industry, h.Weight, h.Shock, h.Contribution }),
                industries = s.Industries
            }),
            monteCarlo = new
            {
                monteCarloValue.Status, monteCarloValue.DataAsOfDate, monteCarloValue.CommonTradingDays,
                monteCarloValue.HorizonDays, monteCarloValue.Simulations, monteCarloValue.Model,
                monteCarloValue.EwmaLambda, monteCarloValue.ShrinkageAlpha,
                monteCarloValue.PositiveReturnProbability, monteCarloValue.ExpectedReturn,
                monteCarloValue.P5FinalReturn, monteCarloValue.P1FinalReturn, monteCarloValue.Diagnostics
            }
        };
        var entity = new RiskReportSnapshot
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            CreatedByUserId = providerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DataAsOfDate = riskValue.DataAsOfDate,
            Model = "MVEWMA-FHS",
            ThresholdVersion = "balanced-v1",
            SnapshotJson = JsonSerializer.Serialize(snapshot)
        };
        _dbContext.RiskReportSnapshots.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<PortfolioRiskReportSnapshotDetailResponse>.Success(ToSnapshotDetail(entity, governanceValue.DataStatus));
    }

    public async Task<Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshotsAsync(
        Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (_dbContext is null)
            return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        if (await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken) is null)
            return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");
        var reports = await _dbContext.RiskReportSnapshots.AsNoTracking()
            .Where(report => report.PortfolioId == portfolioId)
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Success(reports
            .Select(report => new PortfolioRiskReportSnapshotListItemResponse(report.Id, report.CreatedAtUtc, report.DataAsOfDate, report.Model, report.ThresholdVersion, ReadSnapshotStatus(report.SnapshotJson)))
            .ToList());
    }

    public async Task<Result<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshotAsync(
        Guid portfolioId, Guid reportId, Guid providerUserId, CancellationToken cancellationToken)
    {
        if (_dbContext is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.snapshot_unavailable", "Risk report storage is unavailable.");
        if (await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken) is null)
            return Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        var report = await _dbContext.RiskReportSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reportId && item.PortfolioId == portfolioId, cancellationToken);
        return report is null
            ? Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.report_not_found", "Risk report snapshot was not found.")
            : Result<PortfolioRiskReportSnapshotDetailResponse>.Success(ToSnapshotDetail(report, ReadSnapshotStatus(report.SnapshotJson)));
    }

    private static PortfolioRiskReportSnapshotDetailResponse ToSnapshotDetail(RiskReportSnapshot report, string status)
    {
        using var document = JsonDocument.Parse(report.SnapshotJson);
        return new PortfolioRiskReportSnapshotDetailResponse(report.Id, report.PortfolioId, report.CreatedAtUtc, report.DataAsOfDate, report.Model, report.ThresholdVersion, status, document.RootElement.Clone());
    }

    private static string ReadSnapshotStatus(string snapshotJson)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        return document.RootElement.TryGetProperty("dataQuality", out var quality) && quality.TryGetProperty("status", out var status)
            ? status.GetString() ?? "unknown"
            : "unknown";
    }

    public async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken) =>
        await ExecuteRiskOperationAsync(
            "risk.portfolio.stress", null, null, null,
            () => GetPortfolioStressTestCoreAsync(portfolioId, providerUserId, cancellationToken));

    private async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestCoreAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioStressTestResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<PortfolioStressTestResponse>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");
        var ids = portfolio.Holdings.Select(x => x.SecurityId).Distinct().ToList();
        var latest = await _marketPriceRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        var values = await Task.WhenAll(portfolio.Holdings.Where(h => latest.ContainsKey(h.SecurityId)).Select(async h => (h, Value: await _exchangeRateService.ConvertAsync(PortfolioMath.CalculateMarketValue(h.Quantity, latest[h.SecurityId].Price), h.CostCurrency, portfolio.BaseCurrency, cancellationToken))));
        var total = values.Sum(x => x.Value);
        if (total <= 0) return Result<PortfolioStressTestResponse>.Failure("risk.invalid_market_value", "Total market value is zero or negative.");
        var weights = values.ToDictionary(x => x.h.SecurityId, x => x.Value / total);
        var prices = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>();
        foreach (var id in ids) prices[id] = await _marketPriceRepository.GetBySecurityAsync(id, null, null, cancellationToken);
        var scenarios = new List<PortfolioStressScenarioResponse>();
        foreach (var s in new[] { ("covid", "COVID-19 市場急跌", new DateOnly(2020,2,19), new DateOnly(2020,3,23)), ("rates_2022", "2022 升息熊市", new DateOnly(2022,1,3), new DateOnly(2022,10,14)), ("tech_2024", "2024 科技股修正", new DateOnly(2024,7,11), new DateOnly(2024,8,5)) }) scenarios.Add(BuildHistoricalStress(s.Item1,s.Item2,s.Item3,s.Item4,portfolio.Holdings,weights,prices));
        scenarios.Add(BuildWorstTwentyDayStress(portfolio.Holdings, weights, prices));
        var latestById = latest.ToDictionary(x => x.Key, x => x.Value.Price);
        scenarios.Add(BuildHypotheticalStress("taiwan_strait", "台海供應鏈中斷", portfolio.Holdings, weights, latestById, -0.45m, -0.30m, -0.35m));
        scenarios.Add(BuildHypotheticalStress("ai_correction", "AI 泡沫修正", portfolio.Holdings, weights, latestById, -0.30m, -0.15m, -0.20m));
        return Result<PortfolioStressTestResponse>.Success(new(portfolioId, latest.Values.MaxBy(x => x.PriceTime)?.PriceTime is { } date ? DateOnly.FromDateTime(date) : null, scenarios));
    }

    public async Task<Result<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenarioAsync(Guid portfolioId, PortfolioRiskScenarioRequest request, Guid providerUserId, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioRiskScenarioResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<PortfolioRiskScenarioResponse>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");
        var ids = portfolio.Holdings.Select(h => h.SecurityId).Distinct().ToHashSet();
        if (request.TargetWeights.Any(item => !ids.Contains(item.SecurityId))) return Result<PortfolioRiskScenarioResponse>.Failure("risk.invalid_target_weight", "Target weights may only reference existing portfolio holdings.");
        if (request.TargetWeights.Any(item => item.TargetWeight < 0)) return Result<PortfolioRiskScenarioResponse>.Failure("risk.invalid_target_weight", "Target weights cannot be negative.");
        var targetWeights = request.TargetWeights.GroupBy(item => item.SecurityId).ToDictionary(group => group.Key, group => group.Sum(item => item.TargetWeight));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel);
        var scenario = await GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel, targetWeights);
        if (!current.IsSuccess || !scenario.IsSuccess) return Result<PortfolioRiskScenarioResponse>.Failure("risk.insufficient_prices", "正式風險資料不可計算，無法完成目標權重試算。");
        var currentStress = await GetPortfolioStressTestAsync(portfolioId, providerUserId, cancellationToken);
        var scenarioStress = await GetPortfolioStressTestWithWeightsAsync(portfolioId, providerUserId, targetWeights, cancellationToken);
        if (!currentStress.IsSuccess || !scenarioStress.IsSuccess) return Result<PortfolioRiskScenarioResponse>.Failure("risk.insufficient_prices", "壓力測試資料不可計算，無法完成目標權重試算。");
        var cashWeight = 1m - targetWeights.Values.Sum();
        return Result<PortfolioRiskScenarioResponse>.Success(new(portfolioId, cashWeight, current.Value!, scenario.Value!, currentStress.Value!.Scenarios, scenarioStress.Value!.Scenarios, BuildGovernance(portfolioId, current.Value!, 0m, today), BuildGovernance(portfolioId, scenario.Value!, cashWeight, today)));
    }

    private async Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestWithWeightsAsync(Guid portfolioId, Guid providerUserId, IReadOnlyDictionary<Guid, decimal> targetWeights, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioStressTestResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        var ids = portfolio.Holdings.Select(x => x.SecurityId).Distinct().ToList(); var latest = await _marketPriceRepository.GetLatestPricesAsync(ids, DailyInterval, cancellationToken);
        if (latest.Count != ids.Count) return Result<PortfolioStressTestResponse>.Failure("risk.insufficient_prices", "All holdings require latest prices.");
        var prices = new Dictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>>(); foreach (var id in ids) prices[id] = await _marketPriceRepository.GetBySecurityAsync(id, null, null, cancellationToken);
        var scenarios = new List<PortfolioStressScenarioResponse>(); foreach (var s in new[] { ("covid", "COVID-19 市場急跌", new DateOnly(2020,2,19), new DateOnly(2020,3,23)), ("rates_2022", "2022 升息熊市", new DateOnly(2022,1,3), new DateOnly(2022,10,14)), ("tech_2024", "2024 科技股修正", new DateOnly(2024,7,11), new DateOnly(2024,8,5)) }) scenarios.Add(BuildHistoricalStress(s.Item1,s.Item2,s.Item3,s.Item4,portfolio.Holdings,targetWeights,prices));
        scenarios.Add(BuildWorstTwentyDayStress(portfolio.Holdings,targetWeights,prices)); var latestById = latest.ToDictionary(x=>x.Key,x=>x.Value.Price);
        scenarios.Add(BuildHypotheticalStress("taiwan_strait","台海供應鏈中斷",portfolio.Holdings,targetWeights,latestById,-.45m,-.30m,-.35m)); scenarios.Add(BuildHypotheticalStress("ai_correction","AI 泡沫修正",portfolio.Holdings,targetWeights,latestById,-.30m,-.15m,-.20m));
        return Result<PortfolioStressTestResponse>.Success(new(portfolioId, DateOnly.FromDateTime(latest.Values.MaxBy(x=>x.PriceTime)!.PriceTime),scenarios));
    }

    private static PortfolioStressScenarioResponse BuildHistoricalStress(string id, string name, DateOnly from, DateOnly to, IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>> prices)
    {
        var details = new List<PortfolioStressHoldingResponse>();
        foreach (var h in holdings)
        {
            var series = prices.GetValueOrDefault(h.SecurityId) ?? Array.Empty<Contracts.MarketPrices.MarketPriceResponse>();
            var start = series.Where(p => DateOnly.FromDateTime(p.PriceTime) >= from).OrderBy(p => p.PriceTime).FirstOrDefault();
            var end = series.Where(p => DateOnly.FromDateTime(p.PriceTime) <= to).OrderByDescending(p => p.PriceTime).FirstOrDefault();
            if (start is null || end is null || start.Close <= 0) return new(id,name,"historical","insufficient_prices","共同價格不足，無法回放此歷史情境",from,to,0,Array.Empty<PortfolioStressHoldingResponse>(),Array.Empty<PortfolioStressIndustryResponse>());
            var shock = end.Close / start.Close - 1;
            details.Add(new(h.Ticker,h.SecurityName,ResolveIndustry(h),weights.GetValueOrDefault(h.SecurityId),start.Close,end.Close,shock,weights.GetValueOrDefault(h.SecurityId)*shock));
        }
        return BuildStressResponse(id,name,"historical","ready",$"以目前持倉權重回放 {from:yyyy-MM-dd} 至 {to:yyyy-MM-dd} 的價格報酬",from,to,details);
    }

    private static PortfolioStressScenarioResponse BuildWorstTwentyDayStress(IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, IReadOnlyList<Contracts.MarketPrices.MarketPriceResponse>> prices)
    {
        var common = prices.Values.Select(series => series.Select(p => DateOnly.FromDateTime(p.PriceTime)).ToHashSet()).Aggregate((left,right) => { left.IntersectWith(right); return left; }).Order().ToList();
        if (common.Count < 21) return new("worst_20d","資料期間最差 20 日","historical","insufficient_prices","共同價格不足 21 日",null,null,0,Array.Empty<PortfolioStressHoldingResponse>(),Array.Empty<PortfolioStressIndustryResponse>());
        decimal best = 1m; DateOnly from = common[0], to = common[20];
        for (var i=20;i<common.Count;i++) { decimal impact=0; foreach(var h in holdings) { var s=prices[h.SecurityId]; var a=s.First(p=>DateOnly.FromDateTime(p.PriceTime)==common[i-20]).Close; var b=s.First(p=>DateOnly.FromDateTime(p.PriceTime)==common[i]).Close; impact += weights.GetValueOrDefault(h.SecurityId)*(b/a-1); } if(impact<best){best=impact;from=common[i-20];to=common[i];} }
        return BuildHistoricalStress("worst_20d","資料期間最差 20 日",from,to,holdings,weights,prices);
    }

    private static PortfolioStressScenarioResponse BuildHypotheticalStress(string id, string name, IReadOnlyList<EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse> holdings, IReadOnlyDictionary<Guid, decimal> weights, IReadOnlyDictionary<Guid, decimal> latest, decimal semiconductor, decimal finance, decimal other)
    {
        var details = holdings.Select(h => { var industry=ResolveIndustry(h); var shock=industry.Contains("半導體") ? semiconductor : industry.Contains("金融") ? finance : other; var weight=weights.GetValueOrDefault(h.SecurityId); var price=latest.GetValueOrDefault(h.SecurityId); return new PortfolioStressHoldingResponse(h.Ticker,h.SecurityName,industry,weight,price,price*(1+shock),shock,weight*shock); }).ToList();
        return BuildStressResponse(id,name,"hypothetical","ready",$"半導體 {semiconductor:P0}；金融保險 {finance:P0}；其他／未分類 {other:P0}。假設情境，非發生機率預測。",null,null,details);
    }

    private static PortfolioStressScenarioResponse BuildStressResponse(string id,string name,string type,string status,string methodology,DateOnly? from,DateOnly? to,IReadOnlyList<PortfolioStressHoldingResponse> holdings)
    {
        var industries=holdings.GroupBy(x=>x.Industry).Select(g => { var weight=g.Sum(x=>x.Weight); var contribution=g.Sum(x=>x.Contribution); return new PortfolioStressIndustryResponse(g.Key,weight,weight == 0 ? 0 : contribution / weight,contribution); }).OrderBy(x=>x.Contribution).ToList();
        return new(id,name,type,status,methodology,from,to,holdings.Sum(x=>x.Contribution),holdings,industries);
    }
}
