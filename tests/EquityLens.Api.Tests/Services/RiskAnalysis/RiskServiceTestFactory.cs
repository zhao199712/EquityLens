using EquityLens.Api.Data;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.Extensions.Logging;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

internal static class RiskServiceTestFactory
{
    public static RiskAnalysisService Create(
        ISecurityRepository securityRepository,
        IMarketPriceRepository marketPriceRepository,
        IPortfolioRepository portfolioRepository,
        IExchangeRateService exchangeRateService,
        EquityLensDbContext? dbContext = null,
        ILogger<RiskAnalysisService>? logger = null,
        IRiskBacktestEngine? riskBacktestEngine = null)
    {
        var telemetry = new RiskOperationTelemetry(logger);
        var data = new PortfolioRiskDataPreparer(portfolioRepository, marketPriceRepository, exchangeRateService);
        var security = new SecurityRiskCalculator(securityRepository, marketPriceRepository);
        var portfolio = new PortfolioRiskCalculator(data, telemetry);
        var inputs = new RiskBacktestInputProvider(data);
        var backtest = new PortfolioRiskBacktestService(inputs, riskBacktestEngine ?? new CSharpRiskBacktestEngine(), telemetry);
        var monteCarlo = new PortfolioMonteCarloService(data, telemetry);
        var governance = new PortfolioRiskGovernanceService(portfolio, telemetry);
        var stress = new PortfolioStressTestService(portfolioRepository, marketPriceRepository, exchangeRateService, telemetry);
        var scenario = new PortfolioRiskScenarioService(portfolioRepository, portfolio, stress);
        var reports = new PortfolioRiskReportSnapshotService(
            portfolioRepository, portfolio, governance, stress, monteCarlo, backtest, dbContext);
        return new RiskAnalysisService(security, portfolio, backtest, monteCarlo, governance, stress, scenario, reports);
    }
}
