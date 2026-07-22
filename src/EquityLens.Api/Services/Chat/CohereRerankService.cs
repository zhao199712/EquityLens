using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Chat;

public sealed class CohereRerankService : ICohereRerankService
{
    private readonly HttpClient _httpClient;
    private readonly CohereOptions _options;
    private readonly ILogger<CohereRerankService> _logger;

    public CohereRerankService(
        HttpClient httpClient,
        IOptions<CohereOptions> options,
        ILogger<CohereRerankService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CohereRerankResponse> RerankAsync(
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
            top_n = topN,
            return_documents = false
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetByteCount(jsonPayload);
        var stopwatch = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cohere rerank failed with {StatusCode}", response.StatusCode);
                return new CohereRerankResponse(_options.RerankModel, [], "HttpError", $"CohereHttp{(int)response.StatusCode}", stopwatch.ElapsedMilliseconds, documents.Count, payloadBytes, (int)response.StatusCode);
            }

            var doc = JsonDocument.Parse(body);
            var results = new List<CohereRerankItem>();

            if (doc.RootElement.TryGetProperty("results", out var resultsArray))
            {
                foreach (var item in resultsArray.EnumerateArray())
                {
                    var index = item.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
                    var score = item.TryGetProperty("relevance_score", out var s) ? s.GetDouble() : 0.0;
                    results.Add(new CohereRerankItem(index, score));
                }
            }

            var status = results.Count == 0 ? "EmptyResults" : "Succeeded";
            return new CohereRerankResponse(_options.RerankModel, results, status, results.Count == 0 ? "CohereEmptyResults" : null, stopwatch.ElapsedMilliseconds, documents.Count, payloadBytes, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("Cohere rerank timed out after {DurationMs}ms for {CandidateCount} candidates and {PayloadBytes} payload bytes", stopwatch.ElapsedMilliseconds, documents.Count, payloadBytes);
            return new CohereRerankResponse(_options.RerankModel, [], "TimedOut", "CohereTimeout", stopwatch.ElapsedMilliseconds, documents.Count, payloadBytes, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "Cohere rerank network error for query length {QueryLength}, {CandidateCount} candidates and {PayloadBytes} payload bytes", query.Length, documents.Count, payloadBytes);
            return new CohereRerankResponse(_options.RerankModel, [], "NetworkError", "CohereNetworkError", stopwatch.ElapsedMilliseconds, documents.Count, payloadBytes, null);
        }
    }
}
