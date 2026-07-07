using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai;

public sealed class GeminiChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiChatCompletionService(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public string Provider => "gemini";
    public string Model => _options.Model;

    public async Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set Gemini:ApiKey or GEMINI_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("Gemini model is not configured. Set Gemini:Model.");
        }
        if (request.ResponseFormat == ChatResponseFormat.JsonObject)
        {
            throw new NotSupportedException("Gemini chat completion does not support JsonObject response format yet.");
        }

        var endpoint = $"models/{Uri.EscapeDataString(_options.Model)}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Add("x-goog-api-key", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = request.SystemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = request.UserPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxTokens
                }
            }, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini API failed: {(int)response.StatusCode} {responseBody}");
        }

        var result = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("Gemini API returned empty response.");
        var content = result.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Where(part => !string.IsNullOrWhiteSpace(part.Text))
            .Select(part => part.Text)
            .Aggregate(new StringBuilder(), (builder, text) => builder.Append(text))
            .ToString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Gemini API unexpected response: {responseBody}");
        }

        return new ChatCompletionResult(
            content,
            result.ModelVersion ?? _options.Model,
            result.UsageMetadata?.PromptTokenCount ?? 0,
            result.UsageMetadata?.CandidatesTokenCount ?? 0);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record GeminiGenerateContentResponse(
        IReadOnlyList<GeminiCandidate>? Candidates,
        GeminiUsageMetadata? UsageMetadata,
        string? ModelVersion);

    private sealed record GeminiCandidate(GeminiContent? Content);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart>? Parts);
    private sealed record GeminiPart(string? Text);
    private sealed record GeminiUsageMetadata(int PromptTokenCount, int CandidatesTokenCount);
}
