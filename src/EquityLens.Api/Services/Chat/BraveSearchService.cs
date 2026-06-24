using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Chat;

public sealed class BraveSearchService : IBraveSearchService
{
    private readonly HttpClient _httpClient;
    private readonly BraveOptions _options;
    private readonly ILogger<BraveSearchService> _logger;

    public BraveSearchService(
        HttpClient httpClient,
        IOptions<BraveOptions> options,
        ILogger<BraveSearchService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BraveSearchResponse> SearchAsync(
        string query,
        int count = 10,
        string? freshness = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl}?q={Uri.EscapeDataString(query)}&count={count}";
        if (!string.IsNullOrWhiteSpace(freshness))
        {
            url += $"&freshness={Uri.EscapeDataString(freshness)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", _options.ApiKey);
        request.Headers.Add("Accept", "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Brave search failed with {StatusCode}", response.StatusCode);
                return new BraveSearchResponse(query, []);
            }

            var doc = JsonDocument.Parse(body);
            var results = new List<BraveSearchResult>();

            if (doc.RootElement.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var resultsArray))
            {
                foreach (var item in resultsArray.EnumerateArray().Take(count))
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var urlStr = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var pageAge = item.TryGetProperty("page_age", out var p) ? p.GetString() : null;
                    if (pageAge == null)
                    {
                        pageAge = item.TryGetProperty("age", out var a) ? a.GetString() : null;
                    }

                    results.Add(new BraveSearchResult(title, urlStr, description, pageAge));
                }
            }

            return new BraveSearchResponse(query, results);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Brave search error for query: {Query}", query);
            return new BraveSearchResponse(query, []);
        }
    }
}
