using EquityLens.Api.Common;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>提供獨立的回測資料準備入口。</summary>
public sealed class RiskBacktestInputProvider(PortfolioRiskDataPreparer data) : IRiskBacktestInputProvider
{
    public Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken) =>
        data.PreparePortfolioRiskBacktestInputAsync(portfolioId, from, to, providerUserId, cancellationToken);
}
