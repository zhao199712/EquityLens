using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Chat;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class CohereReranker : IDocumentReranker
{
    private readonly ICohereRerankService _cohere;
    private readonly ILogger<CohereReranker> _logger;

    public CohereReranker(
        ICohereRerankService cohere,
        ILogger<CohereReranker> logger)
    {
        _cohere = cohere;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> RerankAsync(
        string query,
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("cohere.rerank");
        activity?.SetTag("rerank.candidate_count", chunks.Count);
        activity?.SetTag("rerank.top_n", topN);

        var documents = chunks.Select(c => c.Result.Content).ToList();
        var response = await _cohere.RerankAsync(query, documents, chunks.Count, cancellationToken);

        if (response.Results.Count == 0)
        {
            _logger.LogWarning("Cohere rerank returned 0 results; falling back to original order");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return chunks.Take(topN).ToList();
        }

        var scoreMap = response.Results.ToDictionary(r => r.Index, r => r.RelevanceScore);
        var minScore = response.Results.Min(r => r.RelevanceScore);
        var maxScore = response.Results.Max(r => r.RelevanceScore);
        var scoreRange = maxScore - minScore;

        var reranked = chunks
            .Select((chunk, originalIndex) =>
            {
                var normalizedScore = scoreRange > 0
                    ? (scoreMap.GetValueOrDefault(originalIndex, 0.0) - minScore) / scoreRange
                    : 0.0;
                return (Chunk: chunk, Score: normalizedScore, OriginalIndex: originalIndex);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.OriginalIndex)
            .Take(topN)
            .Select(x =>
            {
                var result = x.Chunk.Result;
                var updatedResult = new DocumentSearchResult(
                    result.DocumentChunkId, result.DocumentId, result.DocumentTitle,
                    result.DocumentType, result.SourceUrl, result.ChunkIndex, result.PageNumber,
                    result.SectionTitle, result.Content, result.Distance,
                    RelevanceScore: x.Score,
                    result.SecurityId, result.Ticker, result.Exchange, result.SecurityName);
                return new RetrievedDocumentChunk(
                    updatedResult, x.Chunk.SourceRole, x.Chunk.SearchId, x.Chunk.Query,
                    x.Chunk.SourceType, x.Chunk.Url, x.Chunk.PublishedAt, x.Chunk.RetrievedAt);
            })
            .ToList();

        activity?.SetTag("rerank.selected_count", reranked.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return reranked;
    }
}
