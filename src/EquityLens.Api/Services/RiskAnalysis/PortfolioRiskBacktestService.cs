using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>協調回測輸入與既有風險計算引擎。</summary>
public sealed class PortfolioRiskBacktestService(
    IRiskBacktestInputProvider inputs,
    IRiskBacktestEngine engine,
    RiskOperationTelemetry telemetry) : IPortfolioRiskBacktestService
{
    public async Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(
        Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken) =>
        await telemetry.ExecuteAsync(
            "risk.portfolio.backtest", "mvewma_fhs", null, 5000,
            async () =>
            {
                var prepared = await inputs.PreparePortfolioRiskBacktestInputAsync(
                    portfolioId, from, to, providerUserId, cancellationToken);
                if (!prepared.IsSuccess)
                    return Result<PortfolioRiskBacktestResponse>.Failure(
                        prepared.ErrorCode!, prepared.ErrorMessage!);
                return engine.Calculate(prepared.Value!, cancellationToken).ToResult();
            });
}
