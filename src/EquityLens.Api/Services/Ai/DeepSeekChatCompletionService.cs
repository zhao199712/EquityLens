using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai;

public sealed class DeepSeekChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _options;

    public DeepSeekChatCompletionService(HttpClient httpClient, IOptions<DeepSeekOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public string Provider => "deepseek";
    public string Model => _options.Model;

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("DeepSeek API key is not configured. Set DeepSeek:ApiKey or DEEPSEEK_API_KEY.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };
        if (request.ResponseFormat == ChatResponseFormat.JsonObject)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DeepSeek API failed: {(int)response.StatusCode} {responseBody}");
        }

        var result = JsonSerializer.Deserialize<DeepSeekChatResponse>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("DeepSeek API returned empty response.");

        if (result.Choices is not { Count: > 0 } || result.Usage is null)
        {
            throw new InvalidOperationException($"DeepSeek API unexpected response: {responseBody}");
        }

        return new ChatCompletionResult(
            result.Choices[0].Message.Content,
            result.Model ?? _options.Model,
            result.Usage.PromptTokens,
            result.Usage.CompletionTokens);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record DeepSeekChatResponse(
        string? Model,
        IReadOnlyList<DeepSeekChoice>? Choices,
        DeepSeekUsage? Usage);

    private sealed record DeepSeekChoice(DeepSeekMessage Message);
    private sealed record DeepSeekMessage(string Content);
    private sealed record DeepSeekUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
