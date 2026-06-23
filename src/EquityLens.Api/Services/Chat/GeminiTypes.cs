using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityLens.Api.Services.Chat;

internal sealed class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart>? Parts { get; set; }
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("functionCall")]
    public GeminiFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("thoughtSignature")]
    public string? ThoughtSignature { get; set; }
}

internal sealed class GeminiFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public Dictionary<string, JsonElement> Args { get; set; } = new();
}

internal sealed class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }
}
