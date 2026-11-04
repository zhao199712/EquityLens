using System.Net;
using System.Text.Json;
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
