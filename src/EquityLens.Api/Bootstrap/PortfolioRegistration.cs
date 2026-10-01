using EquityLens.Api.Repositories.ExchangeRates;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.DemoData;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.FinancialFilings;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.PortfolioDividends;
using EquityLens.Api.Services.PortfolioFunding;
using EquityLens.Api.Services.PortfolioHoldings;
using EquityLens.Api.Services.PortfolioTransactions;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Services.Portfolios;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Services.Securities;
using EquityLens.Api.Services.UploadedFiles;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Bootstrap;

public static class PortfolioRegistration
{
    public static void AddEquityLensPortfolios(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        builder.Services.AddScoped<ISecurityRepository, SecurityRepository>();
        builder.Services.AddScoped<IPortfolioHoldingRepository, PortfolioHoldingRepository>();
        builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
        builder.Services.AddScoped<IMarketPriceRepository, MarketPriceRepository>();
        builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
        builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
        builder.Services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        builder.Services.AddScoped<IDemoUserContext, DemoUserContext>();
        builder.Services.AddScoped<IDemoDataService, DemoDataService>();
        builder.Services.AddScoped<IPortfolioService, PortfolioService>();
        builder.Services.AddScoped<ISecurityService, SecurityService>();
        builder.Services.AddScoped<IPortfolioHoldingService, PortfolioHoldingService>();
        builder.Services.AddScoped<IPortfolioValuationService, PortfolioValuationService>();
        builder.Services.AddHttpClient<IPortfolioBenchmarkService, FinMindPortfolioBenchmarkService>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<FinMindOptions>>().Value.BaseUrl);
        });
        builder.Services.AddScoped<IPortfolioDividendService, PortfolioDividendService>();
        builder.Services.AddScoped<IMarketPriceService, MarketPriceService>();
        builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
        builder.Services.AddScoped<IUploadedFileService, UploadedFileService>();
        builder.Services.AddScoped<IFinancialFilingService, FinancialFilingService>();
        builder.Services.AddScoped<ITransactionService, TransactionService>();
        builder.Services.AddScoped<IPortfolioFundingService, PortfolioFundingService>();
        builder.Services.AddSingleton<IRiskBacktestEngine, CSharpRiskBacktestEngine>();
        builder.Services.AddScoped<RiskAnalysisService>();
        builder.Services.AddScoped<IRiskAnalysisService>(sp => sp.GetRequiredService<RiskAnalysisService>());
        builder.Services.AddScoped<IRiskBacktestInputProvider>(sp => sp.GetRequiredService<RiskAnalysisService>());
        builder.Services.AddScoped<IRiskBacktestRunService, RiskBacktestRunService>();
        builder.Services.AddScoped<IRiskCalculationRunService, RiskCalculationRunService>();
        builder.Services.AddScoped<IRiskQualityValidationService, RiskQualityValidationService>();
    }
}
