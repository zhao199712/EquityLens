using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;

using static EquityLens.Api.Services.RiskAnalysis.RiskModelDefaults;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>協調目標權重的正式風險、壓力測試與治理比較。</summary>
public sealed class PortfolioRiskScenarioService(
    IPortfolioRepository portfolios,
    IPortfolioRiskCalculator risk,
    IPortfolioStressTestService stress) : IPortfolioRiskScenarioService
{
    public async Task<Result<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenarioAsync(Guid portfolioId, PortfolioRiskScenarioRequest request, Guid providerUserId, CancellationToken cancellationToken)
    {
        var portfolio = await portfolios.GetDetailAsync(portfolioId, providerUserId, cancellationToken);
        if (portfolio is null) return Result<PortfolioRiskScenarioResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        if (portfolio.Holdings.Count == 0) return Result<PortfolioRiskScenarioResponse>.Failure("portfolio.no_holdings", "Portfolio has no holdings.");
        var ids = portfolio.Holdings.Select(h => h.SecurityId).Distinct().ToHashSet();
        if (request.TargetWeights.Any(item => !ids.Contains(item.SecurityId))) return Result<PortfolioRiskScenarioResponse>.Failure("risk.invalid_target_weight", "Target weights may only reference existing portfolio holdings.");
        if (request.TargetWeights.Any(item => item.TargetWeight < 0)) return Result<PortfolioRiskScenarioResponse>.Failure("risk.invalid_target_weight", "Target weights cannot be negative.");
        var targetWeights = request.TargetWeights.GroupBy(item => item.SecurityId).ToDictionary(group => group.Key, group => group.Sum(item => item.TargetWeight));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await risk.GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel);
        var scenario = await risk.GetPortfolioRiskAsync(portfolioId, today.AddYears(-1), today, 1, .95m, 5000, providerUserId, cancellationToken, MvewmaFhsModel, targetWeights);
        if (!current.IsSuccess || !scenario.IsSuccess) return Result<PortfolioRiskScenarioResponse>.Failure("risk.insufficient_prices", "正式風險資料不可計算，無法完成目標權重試算。");
        var currentStress = await stress.GetPortfolioStressTestAsync(portfolioId, providerUserId, cancellationToken);
        var scenarioStress = await stress.GetPortfolioStressTestWithWeightsAsync(portfolioId, providerUserId, targetWeights, cancellationToken);
        if (!currentStress.IsSuccess || !scenarioStress.IsSuccess) return Result<PortfolioRiskScenarioResponse>.Failure("risk.insufficient_prices", "壓力測試資料不可計算，無法完成目標權重試算。");
        var cashWeight = 1m - targetWeights.Values.Sum();
        return Result<PortfolioRiskScenarioResponse>.Success(new(portfolioId, cashWeight, current.Value!, scenario.Value!, currentStress.Value!.Scenarios, scenarioStress.Value!.Scenarios, RiskGovernanceEvaluator.Evaluate(portfolioId, current.Value!, 0m, today), RiskGovernanceEvaluator.Evaluate(portfolioId, scenario.Value!, cashWeight, today)));
    }
}
