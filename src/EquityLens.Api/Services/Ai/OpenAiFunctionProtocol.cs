using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Ai;

/// <summary>將正式 function call 翻譯為既有迴圈決策；不在轉接器執行工具。</summary>
internal static class OpenAiFunctionProtocol
{
    private static readonly JsonObject Answer = JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["markdown","citedEvidenceIds"],"properties":{"markdown":{"type":"string","minLength":1,"maxLength":20000},"citedEvidenceIds":{"type":"array","items":{"type":"string"}}}}
        """)!.AsObject();
    private static readonly JsonObject GiveUp = JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["thoughtSummary"],"properties":{"thoughtSummary":{"type":"string","maxLength":400}}}
        """)!.AsObject();

    public static JsonArray Definitions(ChatToolProtocol protocol)
    {
        var definitions = new JsonArray();
        foreach (var tool in protocol.Tools)
        {
            if (tool.Name is "submit_answer" or "give_up") throw new InvalidOperationException("Reserved tool name.");
            definitions.Add(Function(tool.Name, tool.Description, Strict(tool.Parameters)));
        }
        definitions.Add(Function("submit_answer", "提交有證據支持的最終答案，markdown 內附引用，citedEvidenceIds 列出實際引用編號。", Answer.DeepClone().AsObject()));
        definitions.Add(Function("give_up", "可用工具仍無法取得必要證據時結束；尚未呼叫可用工具不等於資料不足。", GiveUp.DeepClone().AsObject()));
        return definitions;
    }

    private static JsonObject Function(string name, string description, JsonObject schema) => new()
    { ["type"] = "function", ["name"] = name, ["description"] = description, ["parameters"] = schema, ["strict"] = true };

    // Strict schema 不支援任意字典 key；傳輸用 key/value 陣列，執行前還原並檢查重複。
    internal static JsonObject Strict(JsonObject schema)
    {
        if (schema["type"]?.ToString() == "object" && schema["additionalProperties"] is JsonObject values)
            return new JsonObject { ["type"] = "array", ["description"] = "字典項目；key 不可重複。", ["minItems"] = 1,
                ["items"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
                    ["required"] = new JsonArray("key", "value"), ["properties"] = new JsonObject { ["key"] = new JsonObject { ["type"] = "string" }, ["value"] = Strict(values) } } };
        var copy = schema.DeepClone().AsObject();
        if (copy["type"]?.ToString() == "object")
        {
            var required = (schema["required"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToHashSet();
            var properties = new JsonObject();
            foreach (var (key, value) in schema["properties"] as JsonObject ?? new())
            {
                var child = Strict(value!.AsObject());
                if (!required.Contains(key)) child = new JsonObject { ["anyOf"] = new JsonArray(child, new JsonObject { ["type"] = "null" }) };
                properties[key] = child;
            }
            copy["properties"] = properties;
            copy["required"] = new JsonArray(properties.Select(x => (JsonNode?)JsonValue.Create(x.Key)).ToArray());
            copy["additionalProperties"] = false;
        }
        if (copy["items"] is JsonObject item) copy["items"] = Strict(item);
        // const 的單值 enum 等價，且適用供應商支援的 schema 子集。
        if (copy.Remove("const", out var constant)) copy["enum"] = new JsonArray(constant);
        return copy;
    }

    private static JsonNode? Decode(JsonNode? value, JsonObject schema)
    {
        if (value is null) return null;
        if (schema["additionalProperties"] is JsonObject values && schema["type"]?.ToString() == "object")
        {
            var result = new JsonObject();
            foreach (var entry in value.AsArray())
            {
                var key = entry!["key"]!.GetValue<string>();
                if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key)) throw new FormatException();
                result[key] = Decode(entry["value"], values);
            }
            return result;
        }
        if (schema["type"]?.ToString() == "object" && value is JsonObject obj)
        {
            var required = (schema["required"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToHashSet();
            var decoded = new JsonObject();
            foreach (var (key, item) in obj)
            {
                if (item is null && !required.Contains(key)) continue;
                decoded[key] = schema["properties"]?[key] is JsonObject child ? Decode(item, child) : item?.DeepClone();
            }
            return decoded;
        }
        if (schema["items"] is JsonObject items && value is JsonArray array)
            return new JsonArray(array.Select(x => Decode(x, items)).ToArray());
        return value.DeepClone();
    }

    public static string Decision(JsonArray output, ChatToolProtocol protocol)
    {
        try
        {
            var calls = output.OfType<JsonObject>().Where(x => x["type"]?.ToString() == "function_call").ToArray();
            if (calls.Length == 0 || calls.Length > 32) return "{}";
            var ids = calls.Select(x => x["call_id"]!.GetValue<string>()).ToArray();
            if (ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100) || ids.Distinct().Count() != ids.Length) return "{}";
            if (calls.Any(c => c["name"]?.ToString() is "submit_answer" or "give_up"))
            {
                if (calls.Length != 1) return "{}"; // 不執行混合工具與終止決策。
                var name = calls[0]["name"]!.GetValue<string>();
                var args = JsonNode.Parse(calls[0]["arguments"]!.GetValue<string>())!.AsObject();
                return name == "submit_answer"
                    ? new JsonObject { ["action"] = name, ["answer"] = args }.ToJsonString()
                    : new JsonObject { ["action"] = name, ["thoughtSummary"] = args["thoughtSummary"]?.DeepClone() }.ToJsonString();
            }
            var actions = new JsonArray();
            foreach (var call in calls)
            {
                var name = call["name"]!.GetValue<string>();
                var args = JsonNode.Parse(call["arguments"]!.GetValue<string>())!.AsObject();
                var definition = protocol.Tools.SingleOrDefault(x => x.Name == name);
                // 未註冊工具仍交既有 Guard 留下拒絕紀錄，不能因此執行。
                actions.Add(new JsonObject { ["callId"] = call["call_id"]!.DeepClone(), ["tool"] = name,
                    ["arguments"] = definition is null ? args : Decode(args, definition.Parameters) });
            }
            return new JsonObject { ["action"] = "call_tools", ["toolCalls"] = actions }.ToJsonString();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException or NullReferenceException or ArgumentException)
        { return "{}"; } // 原始參數不寫入診斷；由共用協定產生固定錯誤並有界重試。
    }
}
