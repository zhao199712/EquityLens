using System.Text.Json;
using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Agents;

public sealed record AgentToolDefinition(
    string Name,
    string Description,
    object Parameters);

public sealed record AgentResponse(
    string Content,
    IReadOnlyList<ResearchCitation> Citations,
    ResearchRetrievalStrategy? Strategy,
    ResearchTrace? Trace,
    string Status,
    IReadOnlyList<AgentToolCall> ToolCalls,
    string Model,
    int PromptTokens,
    int CompletionTokens);

public sealed record AgentToolCall(
    string ToolName,
    string Arguments,
    string ResultPreview);

public static class AgentArgsExtensions
{
    public static string GetStringOrDefault(
        this Dictionary<string, JsonElement> args, string key, string? defaultValue)
    {
        return args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? defaultValue ?? string.Empty
            : defaultValue ?? string.Empty;
    }

    public static int GetIntOrDefault(
        this Dictionary<string, JsonElement> args, string key, int defaultValue)
    {
        return args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetInt32()
            : defaultValue;
    }

    public static double GetDoubleOrDefault(
        this Dictionary<string, JsonElement> args, string key, double defaultValue)
    {
        return args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : defaultValue;
    }

    public static bool GetBoolOrDefault(
        this Dictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        return args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.True
            ? true
            : element.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }
}
