using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Observability;

namespace EquityLens.Api.Services.Documents;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private const string EmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OpenAiEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public string Model => "text-embedding-3-small";
    public int Dimensions => 1536;

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return [];

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("embedding.create");
        activity?.SetTag("embedding.provider", "openai");
        activity?.SetTag("embedding.model", Model);
        activity?.SetTag("embedding.input_count", inputs.Count);

        try
        {
            var apiKey = _configuration["OpenAI:ApiKey"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OpenAI API key is not configured. Set OpenAI:ApiKey or OPENAI_API_KEY.");

        using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = Model,
            input = inputs,
            dimensions = Dimensions
        }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI embeddings failed: {(int)response.StatusCode} {responseBody}");

        var payload = JsonSerializer.Deserialize<OpenAiEmbeddingsResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("OpenAI embeddings response is empty.");

            var embeddings = payload.Data
                .OrderBy(x => x.Index)
                .Select(x => x.Embedding)
                .ToList();
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            return embeddings;
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            throw;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record OpenAiEmbeddingsResponse(IReadOnlyList<OpenAiEmbeddingData> Data);
    private sealed record OpenAiEmbeddingData(int Index, float[] Embedding);
}
