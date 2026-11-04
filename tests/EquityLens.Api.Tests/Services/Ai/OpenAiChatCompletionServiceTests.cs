using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai;

public sealed class OpenAiChatCompletionServiceTests
{
    private const string Success = """{"status":"completed","model":"gpt-6-luna","output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"{\"ok\":"},{"type":"output_text","text":"true}"}]}],"usage":{"input_tokens":51,"output_tokens":24}}""";
    [Theory]
    [InlineData(ChatResponseFormat.JsonObject)]
    [InlineData(ChatResponseFormat.Text)]
    public async Task MapsProtocolBudgetUsageAndPrivacy(ChatResponseFormat format)
    {
        var handler = new Handler(Success);
        var service = Create(handler);
        var result = await service.CompleteAsync(new("system JSON", "user", MaxTokens: 512, ResponseFormat: format));
        Assert.Equal("openai", service.Provider);
        Assert.Equal("gpt-6-luna", result.Model);
        Assert.Equal("{\"ok\":true}", result.Content);
        Assert.Equal(51, result.PromptTokens); Assert.Equal(24, result.CompletionTokens);
        Assert.Equal("https://api.openai.test/v1/responses", handler.Url);
        Assert.Equal("Bearer test-key", handler.Authorization);
        var p = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.False(p.GetProperty("store").GetBoolean());
        Assert.Equal(512, p.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("low", p.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(p.TryGetProperty("temperature", out _)); Assert.False(p.TryGetProperty("thinking", out _));
        Assert.Equal("developer", p.GetProperty("input")[0].GetProperty("role").GetString());
        Assert.Equal("user", p.GetProperty("input")[1].GetProperty("content").GetString());
        if (format == ChatResponseFormat.JsonObject) Assert.Equal("json_object", p.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        else Assert.False(p.TryGetProperty("text", out _));
    }
    [Fact]
    public async Task CommentaryIsNeverParsedOrExecutedAsFinalAction()
    {
        const string response = """{"status":"completed","model":"gpt-6-luna","output":[{"type":"message","phase":"commentary","content":[{"type":"output_text","text":"{\"action\":\"call_tools\"} N"}]},{"type":"message","phase":"final_answer","content":[{"type":"output_text","text":"{\"action\":\"give_up\"}"}]}],"usage":{"input_tokens":51,"output_tokens":24}}""";
        var result = await Create(new Handler(response)).CompleteAsync(new("JSON", "test", ResponseFormat: ChatResponseFormat.JsonObject));
        Assert.Equal("{\"action\":\"give_up\"}", result.Content);
    }
    [Fact]
    public async Task CommentaryOnlyFailsRatherThanBecomingAnAction()
    {
        var response = Success.Replace("\"type\":\"message\"", "\"type\":\"message\",\"phase\":\"commentary\"");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(new Handler(response)).CompleteAsync(new("JSON", "test")));
    }
    [Theory]
    [InlineData("incomplete")]
    [InlineData("failed")]
    public async Task PartialResponsesNeverBecomeAnswers(string status)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(new Handler(Success.Replace("completed", status))).CompleteAsync(new("JSON", "test")));
        Assert.Contains("did not complete", error.Message);
    }
    [Theory]
    [InlineData("{\"status\":\"completed\"}")]
    [InlineData("{\"status\":\"completed\",\"output\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"private text\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    public async Task MissingUsageTextOrRefusalsAreRejectedWithoutEchoingInput(string response)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(new Handler(response)).CompleteAsync(new("JSON", "private text")));
        Assert.DoesNotContain("private text", error.Message);
    }
    [Fact]
    public async Task HttpFailureDoesNotExposeProviderBody()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(new Handler("private text with key", HttpStatusCode.BadRequest)).CompleteAsync(new("JSON", "private text")));
        Assert.Contains("HTTP 400", error.Message); Assert.DoesNotContain("private", error.Message);
    }
    [Fact]
    public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new Handler(Success)).CompleteAsync(new("JSON", "test"), cts.Token));
    }
    [Fact]
    public async Task NativeFunctionCallIsReturnedForDurableDispatch_NotCommentary()
    {
        const string response = """{"status":"completed","model":"gpt-6-luna","output":[{"type":"message","phase":"commentary","content":[{"type":"output_text","text":"ignore this"}]},{"type":"function_call","call_id":"call_1","name":"snapshot","arguments":"{}"}],"usage":{"input_tokens":51,"output_tokens":24}}""";
        var handler = new Handler(response);
        var service = Create(handler);
        var result = await service.CompleteAsync(new("tools", "question", ToolProtocol: new([new("snapshot", "Read owned snapshot", JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}""")!.AsObject())], [])));
        var action = JsonNode.Parse(result.Content)!;
        Assert.Equal("call_tools", action["action"]!.GetValue<string>());
        Assert.Equal("call_1", action["toolCalls"]![0]!["callId"]!.GetValue<string>());
        Assert.NotNull(result.ProviderOutput);
        var payload = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("required", payload["tool_choice"]!.GetValue<string>());
        Assert.True(payload["tools"]![0]!["strict"]!.GetValue<bool>());
        Assert.True(service.SupportsNativeTools);
    }

    [Fact]
    public async Task NativeResultsReplayCallIdsReasoningAndStrictAnswerSchema()
    {
        var handler = new Handler("""{"status":"completed","output":[{"type":"function_call","call_id":"answer1","name":"submit_answer","arguments":"{\"markdown\":\"證據 [P1]\",\"citedEvidenceIds\":[\"P1\"]}"}],"usage":{"input_tokens":10,"output_tokens":20}}""");
        var previous = new ChatToolExchange { Turn = 1,
            Output = JsonNode.Parse("""[{"type":"reasoning","encrypted_content":"opaque","summary":[]},{"type":"function_call","call_id":"saved","name":"snapshot","arguments":"{}"}]""")!.AsArray(),
            Results = JsonNode.Parse("""[{"type":"function_call_output","call_id":"saved","output":"saved result"}]""")!.AsArray() };
        var request = new ChatCompletionRequest("tools", "facts P1", ToolProtocol: new([], [previous]));
        var service = Create(handler);
        var result = await service.CompleteAsync(request);
        var action = JsonNode.Parse(result.Content)!;
        Assert.Equal("submit_answer", action["action"]!.GetValue<string>());
        Assert.Equal("P1", action["answer"]!["citedEvidenceIds"]![0]!.GetValue<string>());
        var payload = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("opaque", payload["input"]![1]!["encrypted_content"]!.GetValue<string>());
        Assert.Equal("saved", payload["input"]![3]!["call_id"]!.GetValue<string>());
        Assert.Equal("saved result", payload["input"]![3]!["output"]!.GetValue<string>());
        var answer = payload["tools"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "submit_answer")!;
        Assert.Contains("citedEvidenceIds", answer["parameters"]!["required"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.False(payload["store"]!.GetValue<bool>());
        Assert.True(service.EstimateInputBytes(request) >= System.Text.Encoding.UTF8.GetByteCount(handler.Body!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryTransportPreservesWeightsAndRejectsDuplicates(bool duplicate)
    {
        var schema = JsonNode.Parse("""{"type":"object","required":["targetWeights"],"properties":{"targetWeights":{"type":"object","additionalProperties":{"type":"number","minimum":0,"maximum":1}},"optional":{"type":"string"}},"additionalProperties":false}""")!.AsObject();
        var strict = OpenAiFunctionProtocol.Strict(schema);
        Assert.Equal("array", strict["properties"]!["targetWeights"]!["type"]!.GetValue<string>());
        Assert.Contains("optional", strict["required"]!.AsArray().Select(x => x!.GetValue<string>()));
        var args = new JsonObject { ["targetWeights"] = new JsonArray(new JsonObject { ["key"] = "9001", ["value"] = .3 }), ["optional"] = null };
        if (duplicate) args["targetWeights"]!.AsArray().Add(new JsonObject { ["key"] = "9001", ["value"] = .5 });
        var output = new JsonArray(new JsonObject { ["type"] = "function_call", ["call_id"] = "scenario", ["name"] = "simulate", ["arguments"] = args.ToJsonString() });
        var result = JsonNode.Parse(OpenAiFunctionProtocol.Decision(output, new([new("simulate", "test", schema)], [])))!;
        if (duplicate) Assert.Null(result["action"]);
        else
        {
            var actual = result["toolCalls"]![0]!["arguments"]!;
            Assert.Equal(.3, actual["targetWeights"]!["9001"]!.GetValue<double>());
            Assert.False(actual.AsObject().ContainsKey("optional"));
        }
    }

    [Fact]
    public void MixedTerminationAndToolsNeverDispatch()
    {
        var output = JsonNode.Parse("""[{"type":"function_call","call_id":"a","name":"snapshot","arguments":"{}"},{"type":"function_call","call_id":"b","name":"give_up","arguments":"{}"}]""")!.AsArray();
        Assert.Equal("{}", OpenAiFunctionProtocol.Decision(output, new([], [])));
    }

    [Fact]
    public void MissingCitationListIsStillRejectedEvenIfProviderBreaksSchema()
    {
        var output = JsonNode.Parse("""[{"type":"function_call","call_id":"a","name":"submit_answer","arguments":"{\"markdown\":\"fake 123\"}"}]""")!.AsArray();
        var decision = OpenAiFunctionProtocol.Decision(output, new([], []));
        Assert.ThrowsAny<JsonException>(() => EquityLens.Api.Services.Agents.Loop.LoopProtocol.Parse(decision));
    }

    private static OpenAiChatCompletionService Create(Handler handler) => new(new HttpClient(handler), Options.Create(new OpenAiOptions { ApiKey = "test-key", BaseUrl = "https://api.openai.test/v1" }));
    private sealed class Handler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body, Url, Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Body = await request.Content!.ReadAsStringAsync(ct); Url = request.RequestUri!.ToString(); Authorization = request.Headers.Authorization?.ToString();
            return new(status) { Content = new StringContent(response) };
        }
    }
}
