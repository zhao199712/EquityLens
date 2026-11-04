using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai;

/// <summary>Responses API 轉接器，沿用應用程式的 JSON 動作協定與工具 Guard。</summary>
public sealed class OpenAiChatCompletionService : IChatCompletionService
{
    private readonly HttpClient http;
    private readonly OpenAiOptions options;
    private readonly ILogger<OpenAiChatCompletionService>? logger;
    public OpenAiChatCompletionService(HttpClient http, IOptions<OpenAiOptions> options,
        ILogger<OpenAiChatCompletionService>? logger = null)
    {
        this.http = http;
        this.options = options.Value;
        this.logger = logger;
        http.BaseAddress = new Uri(this.options.BaseUrl.TrimEnd('/') + "/");
        if (!new[] { "none", "low", "medium", "high", "xhigh", "max" }.Contains(this.options.ReasoningEffort))
            throw new InvalidOperationException("Unsupported OpenAI reasoning effort.");
    }
    public bool SupportsNativeTools => true;
    public int EstimateInputBytes(ChatCompletionRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(Payload(request))) + 1024;
    public string Provider => "openai";
    public string Model => options.Model;

    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(options.ApiKey) ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : options.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("OpenAI API key is not configured.");
        if (request.MaxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaxTokens));
        var payload = Payload(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            // 供應商錯誤可能含原文／敏感資料，不能寫入 exception、事件或 telemetry。
            throw new InvalidOperationException($"OpenAI API failed: HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("usage", out var usage)
            || !usage.TryGetProperty("input_tokens", out var input) || !input.TryGetInt32(out var inputTokens) || inputTokens < 0
            || !usage.TryGetProperty("output_tokens", out var output) || !output.TryGetInt32(out var outputTokens) || outputTokens < 0)
            throw new InvalidOperationException("OpenAI API response missing valid usage.");
        logger?.LogInformation("LLM usage prompt_tokens={PromptTokens} completion_tokens={CompletionTokens}", inputTokens, outputTokens);
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new InvalidOperationException("OpenAI API response did not complete; partial output cannot be used.");
        if (request.ToolProtocol is { } protocol)
        {
            var outputItems = root.TryGetProperty("output", out var nativeOutput) && nativeOutput.ValueKind == JsonValueKind.Array
                ? JsonNode.Parse(nativeOutput.GetRawText())!.AsArray() : new JsonArray();
            return new(OpenAiFunctionProtocol.Decision(outputItems, protocol),
                root.TryGetProperty("model", out var nativeModel) ? nativeModel.GetString() ?? options.Model : options.Model,
                inputTokens, outputTokens, outputItems);
        }
        var text = new StringBuilder();
        if (root.TryGetProperty("output", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type) || type.GetString() != "message") continue;
                // commentary 是中間說明，不能與最終 JSON 串接或成為可執行的工具決策。
                if (item.TryGetProperty("phase", out var phase) && phase.ValueKind != JsonValueKind.Null
                    && phase.GetString() != "final_answer") continue;
                if (!item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in contents.EnumerateArray())
                {
                    var kind = part.GetProperty("type").GetString();
                    if (kind == "refusal") throw new InvalidOperationException("OpenAI API refused this request.");
                    if (kind == "output_text") text.Append(part.GetProperty("text").GetString());
                }
            }
        if (string.IsNullOrWhiteSpace(text.ToString())) throw new InvalidOperationException("OpenAI API response missing text.");
        return new(text.ToString(), root.TryGetProperty("model", out var model) ? model.GetString() ?? options.Model : options.Model, inputTokens, outputTokens);
    }
    private Dictionary<string, object?> Payload(ChatCompletionRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["input"] = new[] { new { role = "developer", content = request.SystemPrompt }, new { role = "user", content = request.UserPrompt } },
            ["reasoning"] = new { effort = options.ReasoningEffort },
            ["max_output_tokens"] = request.MaxTokens,
            ["store"] = false
        };
        if (request.ToolProtocol is null && request.ResponseFormat == ChatResponseFormat.JsonObject)
            payload["text"] = new { format = new { type = "json_object" } };
        if (request.ToolProtocol is { } protocol)
        {
            var input = new JsonArray(new JsonObject { ["role"] = "developer", ["content"] = request.SystemPrompt });
            foreach (var exchange in protocol.Exchanges)
            {
                foreach (var item in exchange.Output) input.Add(item?.DeepClone());
                foreach (var item in exchange.Results) input.Add(item?.DeepClone());
            }
            input.Add(new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt });
            payload["input"] = input;
            payload["tools"] = OpenAiFunctionProtocol.Definitions(protocol);
            payload["tool_choice"] = "required";
            payload["include"] = new[] { "reasoning.encrypted_content" };
        }
        return payload;
    }

}
