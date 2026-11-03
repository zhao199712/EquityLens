using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>只產生固定欄位路徑與型別診斷，不將模型原文或未知欄位名稱寫入錯誤。</summary>
internal sealed class LoopProtocolException(string code, string fieldPath, string expected, string actual)
    : JsonException($"{code} at {fieldPath}: expected {expected}; received {actual}.")
{
    public object Diagnostic => new { code, field = fieldPath, expected, actual };
}

internal static class LoopProtocol
{
    // 研究與投組共用；範例中的工具／證據必須替換為當輪可用項目，不授予新權限。
    public const string Prompt = """
        動作協定：只輸出一個 JSON object，不用 Markdown code fence。欄位名稱區分大小寫。
        action 是字串；thoughtSummary 是可省略的簡短字串（最多 400 字元）。只輸出目前 action 的欄位。
        call_tools：toolCalls 是非空 array；每項為 object，callId、tool 是字串，arguments 必須是 object，不是字串或 array。
        submit_answer：answer 必須是 object，不能直接放回答字串；answer.markdown 是非空字串，answer.citedEvidenceIds 是字串 array（無引用時為 []）。
        give_up：無法取得足夠證據時使用，不帶 answer 或 toolCalls。
        以下是合法形狀範例；example_tool 與 E1 僅為佔位，須換成當輪可用工具與已存在的證據編號：
        {"action":"call_tools","thoughtSummary":"取得必要資料","toolCalls":[{"callId":"c1","tool":"example_tool","arguments":{}}]}
        {"action":"submit_answer","thoughtSummary":"證據足夠","answer":{"markdown":"有證據支持的敘述 [E1]。","citedEvidenceIds":["E1"]}}
        {"action":"give_up","thoughtSummary":"缺少回答所需證據"}
        若收到 Protocol error，依指出的欄位路徑與 expected 型別修正下一個 JSON；不要重複原錯誤形狀。
        """;

    public static LoopAction Parse(string content, bool restorePreviousVersion = false)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(content); }
        catch (JsonException) { throw new LoopProtocolException("InvalidJson", "$", "valid JSON object", "malformed JSON"); }
        using (document)
        {
            var root = Require(document.RootElement, JsonValueKind.Object, "$");
            var action = Text(Property(root, "action"), "$.action");
            if (action is not ("call_tools" or "submit_answer" or "give_up" or "ask_human"))
                throw Invalid("$.action", "call_tools | submit_answer | give_up | ask_human");
            var summary = root.TryGetProperty("thoughtSummary", out var s) && !(restorePreviousVersion && s.ValueKind == JsonValueKind.Null)
                ? Text(s, "$.thoughtSummary", allowEmpty: true) : "";
            if (summary.Length > 400) throw Invalid("$.thoughtSummary", "string of at most 400 characters");
            List<LoopToolCall> calls = [];
            if (root.TryGetProperty("toolCalls", out var toolCalls) && !(restorePreviousVersion && toolCalls.ValueKind == JsonValueKind.Null))
            {
                Require(toolCalls, JsonValueKind.Array, "$.toolCalls");
                if (toolCalls.GetArrayLength() > 32) throw Invalid("$.toolCalls", "array of at most 32 calls", "array");
                var index = 0;
                foreach (var element in toolCalls.EnumerateArray())
                {
                    var path = $"$.toolCalls[{index++}]";
                    var item = Require(element, JsonValueKind.Object, path);
                    var id = Text(Property(item, "callId"), path + ".callId");
                    if (id.Length > 100 || calls.Any(x => x.CallId == id)) throw Invalid(path + ".callId", "unique string of at most 100 characters");
                    var tool = Text(Property(item, "tool"), path + ".tool");
                    var args = Property(item, "arguments");
                    // v4 已提交決策允許省略 arguments；恢復既有決策時保留原預設，仍經工具 Guard。
                    var arguments = restorePreviousVersion && args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? new JsonObject() : JsonNode.Parse(Require(args, JsonValueKind.Object, path + ".arguments").GetRawText())!.AsObject();
                    calls.Add(new(id, tool, arguments));
                }
            }
            if (action == "call_tools" && calls.Count == 0) throw Invalid("$.toolCalls", "non-empty array", "empty or missing array");
            // 已持久化舊決策可能包含空陣列；非工具動作絕不附帶可執行的工具。
            if (!restorePreviousVersion && action != "call_tools" && calls.Count > 0) throw Invalid("$.toolCalls", "absent or empty array for this action", "non-empty array");
            LoopAnswer? answer = null;
            if (action == "submit_answer")
            {
                var a = Require(Property(root, "answer"), JsonValueKind.Object, "$.answer");
                var markdown = Text(Property(a, "markdown"), "$.answer.markdown");
                if (markdown.Length > 20000) throw Invalid("$.answer.markdown", "string of at most 20000 characters");
                var ids = Property(a, "citedEvidenceIds");
                var evidenceIds = restorePreviousVersion && ids.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? [] : Require(ids, JsonValueKind.Array, "$.answer.citedEvidenceIds").EnumerateArray()
                        .Select((id, i) => Text(id, $"$.answer.citedEvidenceIds[{i}]")).ToArray();
                answer = new(markdown, evidenceIds);
            }
            return new(action, summary, calls, answer);
        }
    }

    private static JsonElement Property(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) ? value : default;
    private static JsonElement Require(JsonElement value, JsonValueKind kind, string path)
    {
        if (value.ValueKind != kind) throw new LoopProtocolException("InvalidType", path, Kind(kind), Kind(value.ValueKind));
        return value;
    }
    private static string Text(JsonElement value, string path, bool allowEmpty = false)
    {
        var text = Require(value, JsonValueKind.String, path).GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text)) throw Invalid(path, "non-empty string");
        return text;
    }
    private static LoopProtocolException Invalid(string path, string expected, string actual = "string") => new("InvalidValue", path, expected, actual);
    private static string Kind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Undefined => "missing", JsonValueKind.True or JsonValueKind.False => "boolean", _ => kind.ToString().ToLowerInvariant()
    };
}
