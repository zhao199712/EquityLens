using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Chat;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class WebRetriever : IWebRetriever
{
    private readonly IBraveSearchService _braveSearch;
    private readonly RetrievalOptions _options;
    private readonly ILogger<WebRetriever> _logger;

    public WebRetriever(
        IBraveSearchService braveSearch,
        Microsoft.Extensions.Options.IOptions<RetrievalOptions> options,
        ILogger<WebRetriever> logger)
    {
        _braveSearch = braveSearch;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveWebAsync(
        string query,
        int count,
        string? freshness,
        CancellationToken cancellationToken = default)
    {
        using var webActivity = EquityLensTelemetry.ActivitySource.StartActivity("web.search");
        webActivity?.SetTag("web.query_length", query.Length);
        webActivity?.SetTag("web.count", count);

        var response = await _braveSearch.SearchAsync(query, count, freshness, cancellationToken);

        if (response.Results.Count == 0)
        {
            webActivity?.SetTag("web.result_count", 0);
            webActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            _logger.LogInformation("Brave search returned 0 results (query length: {QueryLength})", query.Length);
            return [];
        }

        webActivity?.SetTag("web.result_count", response.Results.Count);
        webActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);

        var now = DateTimeOffset.UtcNow;
        var results = response.Results.Select((result, index) =>
        {
            var content = result.Description;
            if (content.Length > _options.WebSearchMaxContentLength)
            {
                content = content[.._options.WebSearchMaxContentLength];
            }

            DateTimeOffset? publishedAt = null;
            if (!string.IsNullOrWhiteSpace(result.PageAge) && DateTimeOffset.TryParse(result.PageAge, out var parsed))
            {
                publishedAt = parsed;
            }

            return new RetrievedDocumentChunk(
                new DocumentSearchResult(
                    DocumentChunkId: Guid.NewGuid(),
                    DocumentId: Guid.NewGuid(),
                    DocumentTitle: result.Title,
                    DocumentType: "WebSearch",
                    SourceUrl: result.Url,
                    ChunkIndex: index + 1,
                    PageNumber: null,
                    SectionTitle: null,
                    Content: content,
                    Distance: 0,
                    RelevanceScore: 0,
                    SecurityId: null,
                    Ticker: null,
                    Exchange: null,
                    SecurityName: null),
                SourceRole: "Web",
                SearchId: "web-search",
                Query: query,
                SourceType: CitationSourceType.Web,
                Url: result.Url,
                PublishedAt: publishedAt,
                RetrievedAt: now);
        }).ToList();

        _logger.LogInformation("Brave search returned {Count} results (query length: {QueryLength})", results.Count, query.Length);
        return results;
    }
}
