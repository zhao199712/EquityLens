using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Chat;

public sealed class JinaSearchService : IJinaSearchService
{
    private readonly HttpClient _httpClient;
    private readonly JinaOptions _options;
    private readonly ILogger<JinaSearchService> _logger;

    public JinaSearchService(
        HttpClient httpClient,
        IOptions<JinaOptions> options,
        ILogger<JinaSearchService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JinaSearchResponse> SearchAsync(
        string query,
        int numResults = 5,
        CancellationToken cancellationToken = default)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://s.jina.ai/{encodedQuery}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-No-Cache", "true");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jina search failed with {StatusCode}", response.StatusCode);
                return new JinaSearchResponse(query, []);
            }

            var results = ParseJinaResponse(body, numResults);
            return new JinaSearchResponse(query, results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jina search error for query: {Query}", query);
            return new JinaSearchResponse(query, []);
        }
    }

    public async Task<JinaRerankResponse> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/rerank";

        var payload = new
        {
            model = _options.RerankModel,
            query,
            documents,
            top_n = topN
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jina rerank failed with {StatusCode}", response.StatusCode);
                return new JinaRerankResponse(_options.RerankModel, []);
            }

            var doc = JsonDocument.Parse(body);
            var results = new List<JinaRerankItem>();

            if (doc.RootElement.TryGetProperty("results", out var resultsArray))
            {
                foreach (var item in resultsArray.EnumerateArray())
                {
                    var index = item.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
                    var score = item.TryGetProperty("relevance_score", out var s) ? s.GetDouble() : 0.0;
                    var text = item.TryGetProperty("document", out var docElement) && docElement.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "";
                    results.Add(new JinaRerankItem(index, score, text));
                }
            }

            return new JinaRerankResponse(_options.RerankModel, results);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Jina rerank error for query: {Query}", query);
            return new JinaRerankResponse(_options.RerankModel, []);
        }
    }

    private static List<JinaSearchResult> ParseJinaResponse(string body, int maxResults)
    {
        var results = new List<JinaSearchResult>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return results;

            foreach (var item in data.EnumerateArray().Take(maxResults))
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var content = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                results.Add(new JinaSearchResult(title, url, content));
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                results.Add(new JinaSearchResult("Web Search Result", "", body.Length > 5000 ? body[..5000] : body));
            }
        }

        return results;
    }
}
