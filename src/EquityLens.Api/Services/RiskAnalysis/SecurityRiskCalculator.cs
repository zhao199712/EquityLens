using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Domain.Calculations;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Securities;

using static EquityLens.Api.Services.RiskAnalysis.PortfolioRiskModelCalculator;
using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>讀取個股價格並計算風險指標。</summary>
public sealed class SecurityRiskCalculator(
    ISecurityRepository securityRepository,
    IMarketPriceRepository marketPriceRepository) : ISecurityRiskCalculator
{
    public async Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
        Guid securityId,
        DateOnly from,
        DateOnly to,
        int horizonDays,
        decimal confidenceLevel,
        int simulations,
        CancellationToken cancellationToken)
    {
        if (!await securityRepository.ActiveExistsAsync(securityId, cancellationToken))
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
        var prices = await marketPriceRepository.GetBySecurityAsync(
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
}
