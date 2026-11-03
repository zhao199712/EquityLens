using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Domain.Calculations;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

internal static class PortfolioRiskModelCalculator
{
    internal static MonteCarloResult RunCorrelatedMonteCarlo(
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

    internal static IReadOnlyList<RiskHorizonResult> BuildSecurityHorizons(
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
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel) : null,
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel) : null,
                    mcResult.SimulatedVaR,
                    mcResult.SimulatedES,
                    mcResult.MeanFinalValue,
                    mcResult.MedianFinalValue,
                    mcResult.WorstCase95Percentile,
                    mcResult.BestCase95Percentile, rollingReturns.Count,
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? "Available" : "InsufficientData", RiskQualityPolicy.MinimumObservations);
            })
            .ToList();
    }

    internal static IReadOnlyList<RiskHorizonResult> BuildPortfolioHorizons(
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
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel) : null,
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel) : null,
                    mcResult.SimulatedVaR,
                    mcResult.SimulatedES,
                    mcResult.MeanFinalValue,
                    mcResult.MedianFinalValue,
                    mcResult.WorstCase95Percentile,
                    mcResult.BestCase95Percentile, rollingReturns.Count,
                    rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? "Available" : "InsufficientData", RiskQualityPolicy.MinimumObservations);
            })
            .ToList();
    }

    internal static decimal CalculateLogReturnVolatility(IReadOnlyList<decimal> prices)
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

    internal static string ResolveIndustry(EquityLens.Api.Contracts.PortfolioHoldings.PortfolioHoldingResponse holding) =>
        IndustryClassification.Normalize(holding.Industry, holding.Sector);

    internal static (
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

    internal static IReadOnlyList<RiskHorizonResult> BuildPortfolioHorizonsMvewmaFhs(
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
                rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateHistoricalVaR(rollingReturns, confidenceLevel) : null,
                rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? RiskMath.CalculateExpectedShortfall(rollingReturns, confidenceLevel) : null,
                fhsResult.SimulatedVaR,
                fhsResult.SimulatedES,
                fhsResult.MeanFinalValue,
                fhsResult.MedianFinalValue,
                fhsResult.WorstCase95Percentile,
                fhsResult.BestCase95Percentile, rollingReturns.Count,
                rollingReturns.Count >= RiskQualityPolicy.MinimumObservations ? "Available" : "InsufficientData", RiskQualityPolicy.MinimumObservations));
        }
        return results;
    }

    internal static (IReadOnlyList<IReadOnlyList<decimal>> ReturnMatrix, IReadOnlyList<decimal> Weights) BuildReturnMatrix(
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
}
