using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Documents;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class DocumentRetriever : IDocumentRetriever
{
    private readonly IDocumentSearchService _documentSearchService;
    private readonly RetrievalOptions _options;

    public DocumentRetriever(
        IDocumentSearchService documentSearchService,
        Microsoft.Extensions.Options.IOptions<RetrievalOptions> options)
    {
        _documentSearchService = documentSearchService;
        _options = options.Value;
    }

    public IReadOnlyList<RetrievedDocumentChunk> GetCandidatesForRerank(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        int targetCount)
    {
        if (targetCount <= 0 || chunks.Count <= targetCount)
        {
            return chunks;
        }

        return [.. chunks
            .OrderByDescending(c => c.Result.RelevanceScore)
            .Take(targetCount)];
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveAsync(
        ResearchRetrievalStrategy strategy,
        string ticker,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RetrievedDocumentChunk>();
        var seenChunkIds = new HashSet<Guid>();

        for (var searchIndex = 0; searchIndex < strategy.Searches.Count; searchIndex++)
        {
            var search = strategy.Searches[searchIndex];
            var searchId = $"search-{searchIndex + 1}";
            using var searchActivity = EquityLensTelemetry.ActivitySource.StartActivity("retrieval.search");
            searchActivity?.SetTag("search.id", searchId);
            searchActivity?.SetTag("source.role", search.SourceRole);
            searchActivity?.SetTag("document.type", search.DocumentType);
            searchActivity?.SetTag("retrieval.top_k", search.TopK);

            DocumentSearchResponse searchResponse;
            try
            {
                searchResponse = await _documentSearchService.SearchAsync(
                    new DocumentSearchRequest(
                        Query: search.Query,
                        Ticker: ticker,
                        DocumentType: search.DocumentType,
                        TopK: Math.Max(search.TopK, _options.LocalCandidateCountForRerank)),
                    cancellationToken);
                searchActivity?.SetTag("candidate.count", searchResponse.Results.Count);
                searchActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception exception)
            {
                EquityLensTelemetry.MarkError(searchActivity, exception);
                throw;
            }

            foreach (var result in searchResponse.Results.OrderByDescending(r => r.RelevanceScore))
            {
                if (seenChunkIds.Add(result.DocumentChunkId))
                {
                    results.Add(new RetrievedDocumentChunk(
                        result,
                        search.SourceRole,
                        searchId,
                        search.Query));
                }
            }
        }

        return results;
    }
}
