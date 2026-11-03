using System.Diagnostics;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Observability;

using static EquityLens.Api.Services.RiskAnalysis.PortfolioRiskModelCalculator;
using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>根據已準備的投組資料計算正式風險指標。</summary>
public sealed class PortfolioRiskCalculator(
    PortfolioRiskDataPreparer data,
    RiskOperationTelemetry telemetry) : IPortfolioRiskCalculator
{
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
        await telemetry.ExecuteAsync(
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
        var prepared = await data.PreparePortfolioRiskAsync(
            portfolioId, from, to, horizonDays, confidenceLevel, simulations,
            providerUserId, cancellationToken, modelName, targetWeights);
        if (!prepared.IsSuccess)
            return Result<PortfolioRiskResponse>.Failure(prepared.ErrorCode!, prepared.ErrorMessage!);
        var input = prepared.Value!;
        var portfolio = input.Portfolio;
        var pricedHoldings = input.PricedHoldings;
        var marketValues = input.MarketValues;
        var totalMarketValue = input.TotalMarketValue;
        var commonDates = input.CommonDates;
        var dateToPrice = input.AlignedPrices;
        var weightsBySecurityId = input.Weights;
        var portfolioReturns = input.PortfolioReturns;

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
                source.IncrementalVolatility, IndustryClassification.Raw(m.Holding.Industry, m.Holding.Sector)));
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
            DailyLogReturns: portfolioReturns,
            MeasurementBasis: new(commonDates.FirstOrDefault(), commonDates.LastOrDefault(),
                targetWeights is null ? "當期已定價持股市值固定權重；缺歷史持股不重配" : "指定固定權重；缺歷史持股不重配",
                "不含帳戶現金；未配置權重視為零報酬", "個股對數報酬加權；回撤由此序列重建",
                "EWMA 年化；風險貢獻採同權重協方差", portfolioReturns.Count,
                "有足夠價格之持股共同交易日；歷史尾部另見每期限有效視窗", confidenceLevel));
        return Result<PortfolioRiskResponse>.Success(response);
    }
}
