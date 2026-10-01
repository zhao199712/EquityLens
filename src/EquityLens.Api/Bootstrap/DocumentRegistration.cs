using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.InvestorConferences;
using EquityLens.Api.Services.Research;

namespace EquityLens.Api.Bootstrap;

public static class DocumentRegistration
{
    public static void AddEquityLensDocuments(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IConferenceImportService, ConferenceImportService>();
        builder.Services.AddScoped<IPdfTextExtractionService, PdfPigTextExtractionService>();
        builder.Services.AddScoped<IConferenceChunkingService, ConferenceChunkingService>();
        builder.Services.AddScoped<IChunkEmbeddingService, ChunkEmbeddingService>();
        builder.Services.AddScoped<IEmbeddingExportService, EmbeddingExportService>();
        builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();
        builder.Services.AddScoped<IResearchPreflightService, ResearchPreflightService>();
        builder.Services.AddScoped<IResearchRunTraceService, ResearchRunTraceService>();
    }
}
