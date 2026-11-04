using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Chat;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Bootstrap;

public static class ResearchAiRegistration
{
    public static void AddEquityLensResearchAi(this WebApplicationBuilder builder)
    {
        // AI / LLM 服務
        builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
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
            case "OPENAI":
                builder.Services.AddHttpClient<IChatCompletionService, OpenAiChatCompletionService>();
                break;
            case "GEMINI":
                builder.Services.AddHttpClient<IChatCompletionService, GeminiChatCompletionService>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported AI:ChatProvider '{chatProvider}'. Supported values: DeepSeek, Gemini, OpenAI.");
        }
        builder.Services.AddScoped<IConversationAgent>(sp => sp.GetRequiredService<InvestmentResearchRouter>());
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
    }
}
