using EquityLens.Api.Bootstrap;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.InvestorConferences;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EquityLens.Api.Tests.Infrastructure;

namespace EquityLens.Api.Tests.Bootstrap;

public sealed class BootstrapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationModules_ResolveRiskAndExecutorAndCoverCatalogNodes(bool e2e)
    {
        var builder = CreateBuilder();
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "bootstrap-test-signing-key-with-at-least-32-characters",
            ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=unused;Username=unused",
            ["Retrieval:RerankProvider"] = "Cohere"
        });
        builder.Configuration["E2E_TEST_MODE"] = e2e ? "1" : "0";
        builder.Configuration["E2E_ADMIN_PASSWORD"] = Guid.NewGuid().ToString("N");
        builder.AddEquityLensInfrastructure();
        builder.AddEquityLensPortfolios();
        builder.AddEquityLensDocuments();
        builder.AddEquityLensAgents(e2e);
        builder.AddEquityLensResearchAi();
        builder.AddEquityLensImportsAndMarketData();
        builder.AddEquityLensHttp();

        // Resolve the real feature graph without connecting to external infrastructure.
        builder.Services.AddSingleton<IExchangeRateService, IdentityExchangeRates>();
        builder.Services.AddSingleton<IRedisCacheService, UnusedCache>();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.IsType<RiskAnalysisService>(services.GetRequiredService<IRiskAnalysisService>());
        Assert.IsType<RiskBacktestInputProvider>(services.GetRequiredService<IRiskBacktestInputProvider>());
        Assert.IsType<AgentRunExecutor>(services.GetRequiredService<IAgentRunExecutor>());
        var catalog = services.GetRequiredService<IAgentWorkflowCatalog>();
        Assert.Equal(e2e, catalog is E2EAgentWorkflowCatalog);
        var registered = services.GetServices<IAgentNodeHandler>().Select(x => x.NodeType).ToArray();
        Assert.Equal(registered.Length, registered.Distinct(StringComparer.Ordinal).Count());
        foreach (var node in catalog.Nodes) Assert.Contains(node.NodeType, registered);

        if (e2e)
        {
            app.MapEquityLensE2E();
            var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints)
                .OfType<RouteEndpoint>().Select(x => x.RoutePattern.RawText).ToArray();
            Assert.Contains("/api/e2e/approval-gate-runs", routes);
            Assert.Contains("/api/e2e/approval-gate-runs/{runId:guid}/execute", routes);
        }
    }

    [Theory]
    [InlineData("DeepSeek", typeof(DeepSeekChatCompletionService))]
    [InlineData("Gemini", typeof(GeminiChatCompletionService))]
    public void ResearchRegistration_SelectsConfiguredChatProvider(string provider, Type expected)
    {
        var builder = CreateBuilder();
        builder.Configuration["AI:ChatProvider"] = provider;
        builder.AddEquityLensResearchAi();
        using var services = builder.Services.BuildServiceProvider();
        Assert.IsType(expected, services.GetRequiredService<IChatCompletionService>());
    }

    [Fact]
    public void ResearchRegistration_RejectsUnsupportedChatProvider()
    {
        var builder = CreateBuilder();
        builder.Configuration["AI:ChatProvider"] = "unsupported";
        Assert.Throws<InvalidOperationException>(() => builder.AddEquityLensResearchAi());
    }

    [Fact]
    public async Task CliDispatcher_PreservesCommandPriority()
    {
        var importer = new CountingConferenceImporter();
        var chunker = new CountingChunker();
        var builder = CreateBuilder();
        builder.Services.AddSingleton<IConferenceImportService>(importer);
        builder.Services.AddSingleton<IConferenceChunkingService>(chunker);
        await using var app = builder.Build();

        Assert.True(await ApplicationCliDispatcher.TryExecuteAsync(app, ["--chunk-conferences", "--import-conferences"]));
        Assert.Equal(1, importer.Calls);
        Assert.Equal(0, chunker.Calls);
        Assert.True(await ApplicationCliDispatcher.TryExecuteAsync(app, ["--chunk-conferences"]));
        Assert.Equal(1, chunker.Calls);
    }

    [Fact]
    public async Task CliDispatcher_PreservesExportDefaultAndExplicitPath()
    {
        var exporter = new RecordingEmbeddingExporter();
        var builder = CreateBuilder();
        builder.Services.AddSingleton<IEmbeddingExportService>(exporter);
        await using var app = builder.Build();

        Assert.True(await ApplicationCliDispatcher.TryExecuteAsync(app, ["--export-embeddings"]));
        Assert.True(await ApplicationCliDispatcher.TryExecuteAsync(app, ["--export-embeddings", "custom.jsonl"]));
        Assert.Equal(new[] { "exports/conference-embeddings.jsonl", "custom.jsonl" }, exporter.Paths);
    }

    [Fact]
    public async Task CliDispatcher_NoCommandOrImportHelp_DoesNotExecuteWork()
    {
        await using var app = CreateBuilder().Build();
        Assert.False(await ApplicationCliDispatcher.TryExecuteAsync(app, []));
        Assert.False(await ApplicationCliDispatcher.TryExecuteAsync(app, ["--help", "--import-finmind-financials", "--import-mops-financials"]));
    }

    [Fact]
    public async Task FinancialCsvExport_PreservesColumnsOrderingAndEscaping()
    {
        await using var db = RefactorTestDbContext.Create();
        var security = new Security { Id = Guid.NewGuid(), Ticker = "2330", Name = "Company, \"quoted\"", Exchange = "TWSE" };
        var statement = new FinancialStatement
        {
            Id = Guid.NewGuid(), Security = security, StatementType = "IncomeStatement",
            PeriodType = "Annual", FiscalYear = 2025, PeriodEndDate = new DateOnly(2025, 12, 31),
            Currency = "TWD", DataSource = "fixture"
        };
        db.FinancialLineItems.AddRange(
            new FinancialLineItem { Id = Guid.NewGuid(), FinancialStatement = statement, Code = "B", Name = "Second", Amount = 2 },
            new FinancialLineItem { Id = Guid.NewGuid(), FinancialStatement = statement, Code = "A", Name = "First", Amount = 1 });
        await db.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"equitylens-csv-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "financials.csv");
        try
        {
            await new FinancialCsvExportService(db).ExportAsync(path);
            var rows = await File.ReadAllLinesAsync(path);
            Assert.Equal(3, rows.Length);
            Assert.Equal("ticker,company_name,exchange,statement_type,period_type,fiscal_year,fiscal_quarter,period_end_date,line_code,line_name,amount,currency,data_source", rows[0]);
            Assert.StartsWith("2330,\"Company, \"\"quoted\"\"\",TWSE,", rows[1]);
            Assert.Contains(",2025-12-31,A,First,1.00,TWD,fixture", rows[1]);
            Assert.Contains(",2025-12-31,B,Second,2.00,TWD,fixture", rows[2]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static WebApplicationBuilder CreateBuilder() => WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [], ContentRootPath = Path.GetTempPath(), EnvironmentName = "Testing"
    });

    private sealed class IdentityExchangeRates : IExchangeRateService
    {
        public Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(amount);
        public Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(1m);
    }

    private sealed class UnusedCache : IRedisCacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Cache reads are not expected during composition.");
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class CountingConferenceImporter : IConferenceImportService
    {
        public int Calls { get; private set; }
        public Task<ConferenceImportResult> ImportAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ConferenceImportResult(0, 0, 0, 0));
        }
    }

    private sealed class CountingChunker : IConferenceChunkingService
    {
        public int Calls { get; private set; }
        public Task<ConferenceChunkingResult> ChunkConferencesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ConferenceChunkingResult(0, 0, 0, 0, 0));
        }
    }

    private sealed class RecordingEmbeddingExporter : IEmbeddingExportService
    {
        public List<string> Paths { get; } = [];
        public Task<EmbeddingExportResult> ExportAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            Paths.Add(outputPath);
            return Task.FromResult(new EmbeddingExportResult(0, outputPath));
        }
    }
}
