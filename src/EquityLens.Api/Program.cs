using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.ExchangeRates;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.Auth;
using EquityLens.Api.Services.CurrentUser;

using EquityLens.Api.Services.DemoData;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.PortfolioDividends;
using EquityLens.Api.Services.PortfolioHoldings;
using EquityLens.Api.Services.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.Securities;
using EquityLens.Api.Services.UploadedFiles;
using EquityLens.Api.Services.FinancialFilings;
using EquityLens.Api.Services.DocumentParsing;
using EquityLens.Api.Services.DocumentProcessing;
using EquityLens.Api.Services.PortfolioTransactions;
using EquityLens.Api.Services.PortfolioFunding;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Services.InvestorConferences;
using EquityLens.Api.Services.FinancialData;
using EquityLens.Api.Services.AdminJobs;
using EquityLens.Api.Services.BackgroundWorkers;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Chat;
using EquityLens.Api.Services.Research;
using EquityLens.Api.Observability;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
var e2eTestMode = string.Equals(builder.Configuration["E2E_TEST_MODE"], "1", StringComparison.Ordinal);

const string serviceName = "equitylens-api";
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(EquityLensTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation(options =>
            {
                options.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    if (request.RequestUri is not null)
                    {
                        activity.SetTag("url.full", request.RequestUri.GetLeftPart(UriPartial.Path));
                    }
                };
            });

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(EquityLensTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resourceBuilder);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
    {
        logging.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
    }
});

// Add services to the container.
builder.Services.AddDbContext<EquityLensDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL"), o => o.UseVector()));

builder.Services.Configure<AlphaVantageOptions>(builder.Configuration.GetSection("MarketData:AlphaVantage"));
builder.Services.Configure<FinMindOptions>(builder.Configuration.GetSection("MarketData:FinMind"));
builder.Services.Configure<AgentRunQueueOptions>(builder.Configuration.GetSection(AgentRunQueueOptions.SectionName));

// Redis 設定與服務註冊
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<RiskPythonOptions>(builder.Configuration.GetSection(RiskPythonOptions.SectionName));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString));
builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
builder.Services.AddScoped<IBackgroundJobQueue, RedisBackgroundJobQueue>();
builder.Services.AddScoped<IAgentRunQueue, RedisAgentRunQueue>();
builder.Services.AddScoped<IRiskPythonShadowQueue, RedisRiskPythonShadowQueue>();
builder.Services.AddScoped<IRiskShadowComparisonService, RiskShadowComparisonService>();

// S3 相容物件儲存設定與服務註冊
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection("ObjectStorage"));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = options.ServiceUrl,
        ForcePathStyle = options.ForcePathStyle,
        AuthenticationRegion = options.Region
    };
    return new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, config);
});
builder.Services.AddScoped<IObjectStorageService, S3ObjectStorageService>();

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
builder.Services.AddScoped<IConferenceImportService, ConferenceImportService>();
builder.Services.AddScoped<IPdfTextExtractionService, PdfPigTextExtractionService>();
builder.Services.AddScoped<IConferenceChunkingService, ConferenceChunkingService>();
builder.Services.AddScoped<IChunkEmbeddingService, ChunkEmbeddingService>();
builder.Services.AddScoped<IEmbeddingExportService, EmbeddingExportService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();
builder.Services.AddScoped<IResearchPreflightService, ResearchPreflightService>();
builder.Services.AddScoped<IResearchRunTraceService, ResearchRunTraceService>();
builder.Services.AddScoped<ICriticReviewAgent, LlmCriticReviewAgent>();
if (e2eTestMode) builder.Services.AddSingleton<IAgentWorkflowCatalog, E2EAgentWorkflowCatalog>();
else builder.Services.AddSingleton<IAgentWorkflowCatalog, AgentWorkflowCatalog>();
builder.Services.AddScoped<IAgentWorkflowAdminService, AgentWorkflowAdminService>();
builder.Services.AddScoped<IAgentApprovalService, AgentApprovalService>();
builder.Services.AddScoped<IPromptManagementService, PromptManagementService>();
builder.Services.AddScoped<IAgentPromptSnapshotService, AgentPromptSnapshotService>();
builder.Services.AddScoped<PromptManagementBootstrapService>();
builder.Services.AddScoped<IDraftRevisionAgent, LlmDraftRevisionAgent>();
builder.Services.AddScoped<IPortfolioDiagnosisNarrativeAgent, LlmPortfolioDiagnosisNarrativeAgent>();
builder.Services.AddScoped<LlmEvidenceRemediationAgent>();
builder.Services.AddScoped<IClaimExtractionAgent>(sp => sp.GetRequiredService<LlmEvidenceRemediationAgent>());
builder.Services.AddSingleton<IClaimSetValidator, ClaimSetValidator>();
builder.Services.AddSingleton<IAnswerQualityValidator, AnswerQualityValidator>();
builder.Services.AddScoped<IEvidenceBackedRevisionAgent>(sp => sp.GetRequiredService<LlmEvidenceRemediationAgent>());
builder.Services.AddScoped<IEvidenceAssessor, LlmEvidenceAssessor>();
builder.Services.AddScoped<IInvestmentReanalysisAgent, LlmInvestmentReanalysisAgent>();
builder.Services.AddScoped<IEvidenceRetrievalPlanAgent, EvidenceRetrievalPlanAgent>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, CriticReviewWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, DraftRevisionWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, ResearchQualityReviewWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, ResearchInvestigationWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, FeedbackRevisionWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, EvidenceRemediationWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, EvidenceReanalysisWorkflowDefinitionProvider>();
if (e2eTestMode)
{
    builder.Services.AddScoped<E2EApprovalGateWorkflowDefinitionProvider>();
    builder.Services.AddScoped<IAgentWorkflowDefinitionProvider>(sp => sp.GetRequiredService<E2EApprovalGateWorkflowDefinitionProvider>());
}
builder.Services.AddScoped<PortfolioDiagnosisWorkflowDefinitionProvider>();
builder.Services.AddScoped<IAgentWorkflowDefinitionProvider>(sp => sp.GetRequiredService<PortfolioDiagnosisWorkflowDefinitionProvider>());
builder.Services.AddSingleton<IWorkflowGraphTopologyService, WorkflowGraphTopologyService>();
builder.Services.AddSingleton<IWorkflowSkillCatalog, WorkflowSkillCatalog>();
builder.Services.AddSingleton<INodeCapabilityRegistry, NodeCapabilityRegistry>();
builder.Services.AddScoped<IPortfolioRiskMathInputProvider, PortfolioRiskMathInputProvider>();
builder.Services.AddScoped<IPortfolioRiskRunResolver, PortfolioRiskRunResolver>();
builder.Services.AddSingleton<IPortfolioRiskMathExecutor, PortfolioRiskMathExecutor>();
builder.Services.AddScoped<IAgentWorkflowPlanner, LlmAgentWorkflowPlanner>();
builder.Services.AddSingleton<IAgentLoopPolicy, ResearchQualityReviewLoopPolicy>();
builder.Services.AddSingleton<IAgentLoopPolicy, PortfolioDiagnosisLoopPolicy>();
builder.Services.AddSingleton<IAgentLoopController, AgentLoopController>();
builder.Services.AddScoped<IInvestmentResearchRouter, InvestmentResearchRouter>();
builder.Services.AddScoped<IWebCapabilityRequestAgent, LlmWebCapabilityRequestAgent>();
builder.Services.AddScoped<IAgentWorkflowQueryService, AgentWorkflowQueryService>();
builder.Services.AddScoped<IDynamicPlanValidator, DynamicPlanValidator>();
builder.Services.AddScoped<IGraphMaterializer, GraphMaterializer>();
builder.Services.AddScoped<IAgentRunGraphValidator, AgentRunGraphValidator>();
builder.Services.AddSingleton<IAgentRunStateMachine, AgentRunStateMachine>();
builder.Services.AddSingleton<IAgentNodeStateMachine, AgentNodeStateMachine>();
builder.Services.AddScoped<IWorkflowPolicyEvaluator, CriticReviewPolicyEvaluator>();
builder.Services.AddScoped<IWorkflowPolicyEvaluator, ResearchQualityReviewPolicyEvaluator>();
builder.Services.AddScoped<IWorkflowPolicyEvaluator, ResearchInvestigationPolicyEvaluator>();
builder.Services.AddScoped<IWorkflowPolicyEvaluator, FeedbackRevisionPolicyEvaluator>();
builder.Services.AddScoped<IWorkflowPolicyEvaluator, EvidenceReanalysisPolicyEvaluator>();
builder.Services.AddScoped<IAgentNodeHandler, LoadResearchRunNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, BuildEvidencePacketNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, CheckEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, CritiqueAnswerNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizeCriticReportNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadCriticReviewRunNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, DraftRevisedAnswerNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizeRevisionNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadPortfolioDiagnosisContextNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ResolvePortfolioRiskEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, PreparePortfolioRiskMathInputsNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ExecutePortfolioRiskMathNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, CalculatePerformanceAttributionNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadRiskProfileNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, PrioritizeRiskAnalysesNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, EvaluatePortfolioDiagnosisQualityNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, BuildPortfolioEvidencePacketNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, DraftPortfolioDiagnosisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizePortfolioDiagnosisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizeRejectedPortfolioDiagnosisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadEvidenceRemediationContextNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, PlanEvidenceRetrievalNodeHandler>();
builder.Services.Configure<JevEvidenceTriageOptions>(builder.Configuration.GetSection(JevEvidenceTriageOptions.SectionName));
builder.Services.Configure<JevRouterShadowOptions>(builder.Configuration.GetSection(JevRouterShadowOptions.SectionName));
builder.Services.Configure<JevClaimEvidenceShadowOptions>(builder.Configuration.GetSection(JevClaimEvidenceShadowOptions.SectionName));
builder.Services.AddHttpClient<TypeSafeDecisionClient>(client =>
    client.BaseAddress = new Uri("https://api.typesafe.ai/"));
builder.Services.AddScoped<IResearchEvidenceTriage, JevResearchEvidenceTriage>();
builder.Services.AddScoped<ResearchEvidenceTriageShadow>();
builder.Services.AddScoped<JevRouterShadow>();
builder.Services.AddScoped<JevClaimEvidenceShadow>();
builder.Services.AddScoped<IAgentNodeHandler, RetrieveRemediationEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, RetrieveWebEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ExtractAnswerClaimsNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, AssessClaimSupportNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ValidateEvidenceMappingsNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, RouteEvidenceRemediationNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, BuildRemediatedEvidencePacketNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, DraftEvidenceBackedRevisionNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizeEvidenceRemediationNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadEvidenceRemediationNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ValidateReanalysisRequestNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, BuildAnalysisContextNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ReanalyzeAnswerNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, CritiqueReanalysisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ReviseReanalysisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, FinalizeReanalysisNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ValidateResearchRequestNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, DetectResearchIntentNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, PlanResearchRetrievalNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, RetrieveLocalResearchEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, EvaluateInitialEvidencePolicyNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, RetrieveWebResearchEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, RankAndSelectResearchEvidenceNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, DraftResearchAnswerNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, LoadFeedbackRevisionContextNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, ValidateFeedbackRevisionContextNodeHandler>();
builder.Services.AddScoped<IAgentNodeHandler, WaitForHumanApprovalNodeHandler>();
if (e2eTestMode) builder.Services.AddScoped<IAgentNodeHandler, E2EApprovalGateNodeHandler>();
builder.Services.AddScoped<IAgentRunExecutor, AgentRunExecutor>();
builder.Services.AddScoped<IAgentRunService, AgentRunService>();
    builder.Services.AddScoped<IBackgroundJobExecutor, BackgroundJobExecutor>();
    builder.Services.AddHostedService<BackgroundJobWorker>();
    builder.Services.AddHostedService<AgentRunWorker>();
    builder.Services.AddHostedService<AgentRunWakeOutboxDispatcher>();
    builder.Services.AddHostedService<RiskPythonShadowResultWorker>();
    builder.Services.AddHostedService<RiskPythonTimeoutWorker>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();

// AI / LLM 服務
builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection("DeepSeek"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<RetrievalOptions>(builder.Configuration.GetSection(RetrievalOptions.SectionName));
builder.Services.Configure<JinaOptions>(builder.Configuration.GetSection(JinaOptions.SectionName));
builder.Services.Configure<CohereOptions>(builder.Configuration.GetSection(CohereOptions.SectionName));
builder.Services.Configure<BraveOptions>(builder.Configuration.GetSection(BraveOptions.SectionName));

// Retrieval pipeline
builder.Services.AddScoped<IIntentDetector, IntentDetector>();
builder.Services.AddScoped<IRetrievalPlanner, RetrievalPlanner>();
builder.Services.AddScoped<IDocumentRetriever, DocumentRetriever>();
builder.Services.AddScoped<IWebRetriever, WebRetriever>();
var rerankProvider = builder.Configuration["Retrieval:RerankProvider"]?.Trim();
switch (rerankProvider)
{
    case "Cohere":
        builder.Services.AddScoped<IDocumentReranker, CohereReranker>();
        break;
    case "Jina":
        builder.Services.AddScoped<IDocumentReranker, JinaReranker>();
        break;
}
builder.Services.AddScoped<IChunkContentCleaner, ChunkContentCleaner>();
builder.Services.AddScoped<IResultReranker, ResultReranker>();
builder.Services.AddScoped<IContextSelector, ContextSelector>();
builder.Services.AddScoped<IContextFormatter, ContextFormatter>();
builder.Services.AddScoped<ICitationValidator, CitationValidator>();
builder.Services.AddScoped<IAnswerGenerator, AnswerGenerator>();

var chatProvider = builder.Configuration["AI:ChatProvider"]?.Trim();
switch (chatProvider?.ToUpperInvariant())
{
    case null or "" or "DEEPSEEK":
        builder.Services.AddHttpClient<IChatCompletionService, DeepSeekChatCompletionService>();
        break;
    case "GEMINI":
        builder.Services.AddHttpClient<IChatCompletionService, GeminiChatCompletionService>();
        break;
    default:
        throw new InvalidOperationException(
            $"Unsupported AI:ChatProvider '{chatProvider}'. Supported values: DeepSeek, Gemini.");
}
builder.Services.AddScoped<IConversationAgent, LlmConversationAgent>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IResearchAnswerService, ResearchAnswerService>();

// Agentic RAG Chat 服務
builder.Services.AddHttpClient<IJinaSearchService, JinaSearchService>();
builder.Services.AddHttpClient<IBraveSearchService, BraveSearchService>();
builder.Services.AddHttpClient<ICohereRerankService, CohereRerankService>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CohereOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60));
});

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

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EquityLens.Api",
        Version = "v1"
    });

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });
});

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://127.0.0.1:5173",
                  "http://127.0.0.1:5174",
                  "http://192.168.50.11:5173",
                  "http://172.25.14.202:5173",
                  "https://equitylens.pages.dev")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// CurrentUser context
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();

var app = builder.Build();

// Prompt baseline is data, not a runtime fallback. It only initializes databases upgraded
// from the pre-management schema; execution always resolves persisted bindings/snapshots.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PromptManagementBootstrapService>().EnsureSeededAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapControllers();
if (e2eTestMode)
{
    app.MapPost("/api/e2e/approval-gate-runs", async (
        ICurrentUserContext currentUser,
        E2EApprovalGateWorkflowDefinitionProvider provider,
        IAgentWorkflowAdminService workflowAdmin,
        IAgentWorkflowCatalog catalog,
        EquityLensDbContext db,
        IAgentRunQueue queue,
        CancellationToken cancellationToken) =>
    {
        await workflowAdmin.EnsureEnabledAsync(E2EApprovalGateWorkflow.WorkflowType, cancellationToken);
        var run = provider.CreateRun(currentUser.UserId, Guid.NewGuid());
        var policies = await workflowAdmin.GetPoliciesAsync([E2EApprovalGateWorkflow.NodeType], cancellationToken);
        var approvals = await workflowAdmin.GetApprovalPoliciesAsync([E2EApprovalGateWorkflow.NodeType], cancellationToken);
        var entry = catalog.GetNode(E2EApprovalGateWorkflow.NodeType);
        var root = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
        var node = root["nodes"]!.AsArray()[0]!.AsObject();
        var policy = policies[E2EApprovalGateWorkflow.NodeType];
        node["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = policy.TimeoutSeconds, ["maxRetryCount"] = policy.MaxRetryCount };
        node["contract"] = JsonSerializer.SerializeToNode(entry.Contract, AgentNodeJson.SerializerOptions);
        node["approvalPolicy"] = new JsonObject
        {
            ["requiresHumanApproval"] = approvals[E2EApprovalGateWorkflow.NodeType],
            ["sideEffectLevel"] = entry.SideEffectLevel,
            ["reason"] = "E2E deterministic side effect requires approval."
        };
        run.WorkflowDefinitionJson = root.ToJsonString(AgentNodeJson.SerializerOptions);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(new AgentRunQueueMessage(run.Id, run.UserId, run.WorkflowType, DateTime.UtcNow), cancellationToken);
        return Results.Accepted($"/api/agent-runs/{run.Id}", new { agentRunId = run.Id, run.Status });
    }).RequireAuthorization(policy => policy.RequireRole("Admin"));

    app.MapPost("/api/e2e/approval-gate-runs/{runId:guid}/execute", async (
        Guid runId,
        ICurrentUserContext currentUser,
        IAgentRunExecutor executor,
        EquityLensDbContext db,
        CancellationToken cancellationToken) =>
    {
        var exists = await db.AgentRuns.AnyAsync(
            x => x.Id == runId && x.UserId == currentUser.UserId && x.WorkflowType == E2EApprovalGateWorkflow.WorkflowType,
            cancellationToken);
        if (!exists) return Results.NotFound();
        await executor.ExecuteAsync(runId, currentUser.UserId, cancellationToken);
        return Results.NoContent();
    }).RequireAuthorization(policy => policy.RequireRole("Admin"));
}

// 匯入法說會模式: dotnet run -- --import-conferences
if (args.Contains("--import-conferences"))
{
    using var scope = app.Services.CreateScope();
    var importService = scope.ServiceProvider.GetRequiredService<IConferenceImportService>();
    Console.WriteLine("開始匯入法說會 PDF 到 Garage...");
    var result = await importService.ImportAllAsync();
    Console.WriteLine($"===== 匯入完成 =====");
    Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped}");
    return;
}

// 法說會 PDF 切 chunk 模式: dotnet run -- --chunk-conferences
if (args.Contains("--chunk-conferences"))
{
    using var scope = app.Services.CreateScope();
    var chunkingService = scope.ServiceProvider.GetRequiredService<IConferenceChunkingService>();
    Console.WriteLine("開始解析法說會 PDF 並寫入 document_chunk...");
    var result = await chunkingService.ChunkConferencesAsync();
    Console.WriteLine("===== chunk 完成 =====");
    Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped} | chunks: {result.ChunksCreated}");
    return;
}

// 產生缺少的 embedding: dotnet run -- --embed-chunks
if (args.Contains("--embed-chunks"))
{
    using var scope = app.Services.CreateScope();
    var embeddingService = scope.ServiceProvider.GetRequiredService<IChunkEmbeddingService>();
    Console.WriteLine("開始產生 document_embedding...");
    var result = await embeddingService.EmbedMissingChunksAsync();
    Console.WriteLine("===== embedding 完成 =====");
    Console.WriteLine($"待處理: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 未處理: {result.Skipped}");
    return;
}

// 匯出 embedding JSONL: dotnet run -- --export-embeddings ./exports/conference-embeddings.jsonl
if (args.Contains("--import-finmind-financials") && !args.Contains("--help"))
{
    var fromIndex = Array.IndexOf(args, "--from");
    var toIndex = Array.IndexOf(args, "--to");
    var from = fromIndex >= 0 && fromIndex + 1 < args.Length ? DateOnly.Parse(args[fromIndex + 1]) : new DateOnly(2023, 1, 1);
    var to = toIndex >= 0 && toIndex + 1 < args.Length ? DateOnly.Parse(args[toIndex + 1]) : DateOnly.FromDateTime(DateTime.Today);

    using var scope = app.Services.CreateScope();
    var importService = scope.ServiceProvider.GetRequiredService<IFinMindFinancialImportService>();
    Console.WriteLine($"開始從 FinMind 匯入財報結構化數字 ({from:yyyy-MM-dd} ~ {to:yyyy-MM-dd})...");
    var result = await importService.ImportAsync(from, to);
    Console.WriteLine("===== 匯入完成 =====");
    Console.WriteLine($"Statements: {result.TotalStatements} | LineItems: {result.TotalLineItems} | 成功: {result.Succeeded} | 失敗: {result.Failed}");
    return;
}

// MOPS 匯入財報模式: dotnet run -- --import-mops-financials
if (!args.Contains("--help") && args.Contains("--import-mops-financials"))
{
    var fromIndex = Array.IndexOf(args, "--from");
    var toIndex = Array.IndexOf(args, "--to");
    var from = fromIndex >= 0 && fromIndex + 1 < args.Length ? DateOnly.Parse(args[fromIndex + 1]) : new DateOnly(2023, 1, 1);
    var to = toIndex >= 0 && toIndex + 1 < args.Length ? DateOnly.Parse(args[toIndex + 1]) : DateOnly.FromDateTime(DateTime.Today);

    using var scope = app.Services.CreateScope();
    var importService = scope.ServiceProvider.GetRequiredService<IMopsFinancialImportService>();
    Console.WriteLine($"開始從 MOPS 匯入財報結構化數字 ({from:yyyy-MM-dd} ~ {to:yyyy-MM-dd})...");
    var result = await importService.ImportAsync(from, to);
    Console.WriteLine("===== 匯入完成 =====");
    Console.WriteLine($"Statements: {result.StatementsCreated} | LineItems: {result.LineItemsCreated} | 成功: {result.Succeeded} | 失敗: {result.Failed}");
    return;
}

// 下載 TWSE 財報年報 PDF: dotnet run -- --download-twse-reports
if (args.Contains("--download-twse-reports"))
{
    using var scope = app.Services.CreateScope();
    var downloadService = scope.ServiceProvider.GetRequiredService<ITwseReportDownloadService>();

    // Resolve default output relative to repo root (walk up from src/EquityLens.Api)
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var defaultOutput = Path.Combine(repoRoot, "exports", "financial-reports");
    var outputDir = args.Contains("--output")
        ? Path.GetFullPath(args[Array.IndexOf(args, "--output") + 1])
        : defaultOutput;

    var result = await downloadService.DownloadAnnualReportsAsync([112, 113, 114], outputDir);
    Console.WriteLine($"===== 下載完成 =====");
    Console.WriteLine($"嘗試: {result.Attempted} | 成功: {result.Succeeded} | 略過: {result.Skipped} | 失敗: {result.Failed}");
    Console.WriteLine($"Manifest: {result.ManifestPath}");
    return;
}

// 上傳已下載的 TWSE 年報 PDF 到 Garage: dotnet run -- --import-twse-report-files
if (args.Contains("--import-twse-report-files"))
{
    using var scope = app.Services.CreateScope();
    var importService = scope.ServiceProvider.GetRequiredService<ITwseReportFileImportService>();
    Console.WriteLine("開始上傳 TWSE 年報 PDF 到 Garage 並寫入 metadata...");
    var result = await importService.ImportAllAsync();
    Console.WriteLine("===== 匯入完成 =====");
    Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped}");
    return;
}

// 匯出 embedding JSONL: dotnet run -- --export-embeddings ./exports/conference-embeddings.jsonl
if (args.Contains("--export-embeddings"))
{
    var index = Array.IndexOf(args, "--export-embeddings");
    var outputPath = index >= 0 && index + 1 < args.Length
        ? args[index + 1]
        : "exports/conference-embeddings.jsonl";

    using var scope = app.Services.CreateScope();
    var exportService = scope.ServiceProvider.GetRequiredService<IEmbeddingExportService>();
    Console.WriteLine($"開始匯出 document_embedding 到 {outputPath}...");
    var result = await exportService.ExportAsync(outputPath);
    Console.WriteLine("===== 匯出完成 =====");
    Console.WriteLine($"筆數: {result.Exported} | 路徑: {result.OutputPath}");
    return;
}

// 匯出財務結構化數字 CSV: dotnet run -- --export-financials-csv ./exports/financials.csv
if (args.Contains("--export-financials-csv"))
{
    var idx = Array.IndexOf(args, "--export-financials-csv");
    var csvPath = idx >= 0 && idx + 1 < args.Length
        ? args[idx + 1]
        : "exports/financial-statements.csv";

    var fullPath = Path.GetFullPath(csvPath);
    var dir = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();

    await using var writer = new StreamWriter(fullPath, false, Encoding.UTF8);
    await writer.WriteLineAsync("ticker,company_name,exchange,statement_type,period_type,fiscal_year,fiscal_quarter,period_end_date,line_code,line_name,amount,currency,data_source");

    var query = from fli in db.FinancialLineItems
                join fs in db.FinancialStatements on fli.FinancialStatementId equals fs.Id
                join s in db.Securities on fs.SecurityId equals s.Id
                orderby s.Ticker, fs.StatementType, fs.FiscalYear, fs.FiscalQuarter, fli.Code
                select new
                {
                    Ticker = s.Ticker,
                    CompanyName = s.Name,
                    Exchange = s.Exchange,
                    StatementType = fs.StatementType,
                    PeriodType = fs.PeriodType,
                    FiscalYear = fs.FiscalYear,
                    FiscalQuarter = fs.FiscalQuarter,
                    PeriodEndDate = fs.PeriodEndDate,
                    LineCode = fli.Code,
                    LineName = fli.Name,
                    Amount = fli.Amount,
                    Currency = fs.Currency,
                    DataSource = fs.DataSource
                };

    var count = 0;
    await foreach (var row in query.AsAsyncEnumerable())
    {
        await writer.WriteAsync($"{EscapeCsv(row.Ticker)},{EscapeCsv(row.CompanyName)},{EscapeCsv(row.Exchange)},");
        await writer.WriteAsync($"{EscapeCsv(row.StatementType)},{EscapeCsv(row.PeriodType)},{row.FiscalYear},");
        await writer.WriteAsync($"{row.FiscalQuarter},{row.PeriodEndDate:yyyy-MM-dd},");
        await writer.WriteAsync($"{EscapeCsv(row.LineCode)},{EscapeCsv(row.LineName)},{row.Amount:F2},");
        await writer.WriteLineAsync($"{EscapeCsv(row.Currency)},{EscapeCsv(row.DataSource)}");
        count++;
    }

    Console.WriteLine($"===== 匯出完成 =====");
    Console.WriteLine($"筆數: {count} | 路徑: {fullPath}");
    return;
}

static string EscapeCsv(string? value) =>
    value is null ? "" :
    value.Contains(',') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

if (e2eTestMode)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
    const string email = "playwright-admin-test@example.com";
    const string password = "Test1234!";
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null)
    {
        user = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = "Playwright E2E Admin", Role = "Admin", IsActive = true };
        db.Users.Add(user);
    }
    user.Role = "Admin";
    user.IsActive = true;
    user.PasswordHash = passwordHasher.HashPassword(user, password);
    user.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
}

app.Run();
