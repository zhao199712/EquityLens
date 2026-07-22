using System.Diagnostics;
using System.Text;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Chat;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class JinaReranker : IDocumentReranker
{
    private readonly IJinaSearchService _jinaSearch;
    private readonly ILogger<JinaReranker> _logger;

    public JinaReranker(
        IJinaSearchService jinaSearch,
        ILogger<JinaReranker> logger)
    {
        _jinaSearch = jinaSearch;
        _logger = logger;
    }

    public async Task<DocumentRerankResult> RerankAsync(
        string query,
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return new([], new("Jina", "Skipped", null, false, null, 0, 0, 0, 0, null));
        }

        using var rerankActivity = EquityLensTelemetry.ActivitySource.StartActivity("jina.rerank");
        rerankActivity?.SetTag("rerank.candidate_count", chunks.Count);
        rerankActivity?.SetTag("rerank.top_n", topN);

        var documents = chunks.Select(c => c.Result.Content).ToList();
        var stopwatch = Stopwatch.StartNew();
        var payloadBytes = Encoding.UTF8.GetByteCount(query) + documents.Sum(document => Encoding.UTF8.GetByteCount(document));
        var response = await _jinaSearch.RerankAsync(query, documents, chunks.Count, cancellationToken);
        stopwatch.Stop();

        if (response.Results.Count == 0)
        {
            _logger.LogWarning("Jina rerank returned 0 results; falling back to original order");
            rerankActivity?.SetStatus(ActivityStatusCode.Ok);
            var fallback = chunks.Take(topN).ToList();
            return new(fallback, new("Jina", "EmptyResults", response.Model, true, "JinaEmptyResults", stopwatch.ElapsedMilliseconds, chunks.Count, fallback.Count, payloadBytes, null));
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
                // Update relevance score with rerank score
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

        rerankActivity?.SetTag("rerank.selected_count", reranked.Count);
        rerankActivity?.SetStatus(ActivityStatusCode.Ok);

        return new(reranked, new("Jina", "Succeeded", response.Model, false, null, stopwatch.ElapsedMilliseconds, chunks.Count, reranked.Count, payloadBytes, null));
    }
}
