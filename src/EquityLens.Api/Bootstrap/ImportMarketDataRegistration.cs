using EquityLens.Api.Services.AdminJobs;
using EquityLens.Api.Services.DocumentParsing;
using EquityLens.Api.Services.DocumentProcessing;
using EquityLens.Api.Services.FinancialData;
using EquityLens.Api.Services.FinancialFilings;
using EquityLens.Api.Services.MarketData;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Bootstrap;

public static class ImportMarketDataRegistration
{
    public static void AddEquityLensImportsAndMarketData(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IFinMindFinancialImportService, FinMindFinancialImportService>();
        builder.Services.AddHttpClient<FinMindFinancialImportService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<FinMindOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });
        builder.Services.AddHttpClient<IFinMindDividendImportService, FinMindDividendImportService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<FinMindOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        builder.Services.AddScoped<IMopsFinancialImportService, MopsFinancialImportService>();
        builder.Services.AddHttpClient<MopsFinancialImportService>(client =>
        {
            client.BaseAddress = new Uri("https://mopsov.twse.com.tw");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddScoped<ITwseReportDownloadService, TwseReportDownloadService>();
        builder.Services.AddScoped<ITwseReportFileImportService, TwseReportFileImportService>();
        builder.Services.AddHttpClient<TwseReportDownloadService>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // 富櫃50資料來源（不可作為元大0050成分股來源）
        builder.Services.AddHttpClient<TpeiTaiwan50Provider>();
        builder.Services.AddScoped<ITaiwan50ConstituentProvider, TpeiTaiwan50Provider>();
        builder.Services.AddScoped<ITw0050PriceSyncService, Tw0050PriceSyncService>();
        builder.Services.AddHttpClient<TwseFilingCrawler>();
        builder.Services.AddScoped<ITwseFilingCrawler, TwseFilingCrawler>();
        builder.Services.AddScoped<IPdfParser, PigPdfParser>();
        builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

        builder.Services.AddHttpClient<AlphaVantageMarketDataProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AlphaVantageOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        builder.Services.AddHttpClient<FinMindMarketDataProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<FinMindOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        builder.Services.AddHttpClient<YahooFinanceMarketDataProvider>(client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });

        builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<YahooFinanceMarketDataProvider>());
        builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<AlphaVantageMarketDataProvider>());
        builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<FinMindMarketDataProvider>());
        builder.Services.AddTransient<ICorporateActionProvider>(sp => sp.GetRequiredService<FinMindMarketDataProvider>());
    }
}
