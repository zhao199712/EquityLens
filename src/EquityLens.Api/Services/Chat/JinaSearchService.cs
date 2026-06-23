using System.Text;
using System.Text.Json;

namespace EquityLens.Api.Services.Chat;

public sealed class JinaSearchService : IJinaSearchService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JinaSearchService> _logger;

    public JinaSearchService(HttpClient httpClient, ILogger<JinaSearchService> logger)
    {
        _httpClient = httpClient;
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

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jina search failed: {StatusCode}", response.StatusCode);
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
            // If JSON parsing fails, return raw content as single result
            if (!string.IsNullOrWhiteSpace(body))
            {
                results.Add(new JinaSearchResult("Web Search Result", "", body.Length > 5000 ? body[..5000] : body));
            }
        }

        return results;
    }
}
