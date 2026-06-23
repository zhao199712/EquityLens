using System.Text.Json;

namespace EquityLens.Api.Services.Chat;

internal static class JsonElementExtensions
{
    public static string GetStringOrDefault(this Dictionary<string, JsonElement> args, string key, string? defaultValue)
    {
        if (args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? defaultValue ?? "";
        return defaultValue ?? "";
    }

    public static int GetIntOrDefault(this Dictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (args.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var val))
                return val;
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }

    public static int? GetNullableIntOrDefault(this Dictionary<string, JsonElement> args, string key, int? defaultValue)
    {
        if (args.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                return null;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var val))
                return val;
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                return parsed;
        }
        return defaultValue;
    }
}
