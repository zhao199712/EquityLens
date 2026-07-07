using System.Net;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai;

public sealed class DeepSeekChatCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_JsonObjectResponseFormat_AddsDeepSeekResponseFormat()
    {
        var handler = new CaptureHttpMessageHandler(
            """
            {
              "model": "deepseek-test",
              "choices": [
                { "message": { "content": "{\"summary\":\"ok\"}" } }
              ],
              "usage": { "prompt_tokens": 3, "completion_tokens": 4 }
            }
            """);
        var service = new DeepSeekChatCompletionService(
            new HttpClient(handler),
            Options.Create(new DeepSeekOptions
            {
                BaseUrl = "https://api.deepseek.test",
                ApiKey = "test-key",
                Model = "deepseek-test"
            }));

        await service.CompleteAsync(new ChatCompletionRequest(
            "system json",
            "user",
            ResponseFormat: ChatResponseFormat.JsonObject));

        Assert.NotNull(handler.RequestBody);
        using var document = JsonDocument.Parse(handler.RequestBody);
        var responseFormat = document.RootElement.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CompleteAsync_TextResponseFormat_OmitsResponseFormat()
    {
        var handler = new CaptureHttpMessageHandler(
            """
            {
              "model": "deepseek-test",
              "choices": [
                { "message": { "content": "plain text" } }
              ],
              "usage": { "prompt_tokens": 3, "completion_tokens": 4 }
            }
            """);
        var service = new DeepSeekChatCompletionService(
            new HttpClient(handler),
            Options.Create(new DeepSeekOptions
            {
                BaseUrl = "https://api.deepseek.test",
                ApiKey = "test-key",
                Model = "deepseek-test"
            }));

        await service.CompleteAsync(new ChatCompletionRequest("system", "user"));

        Assert.NotNull(handler.RequestBody);
        using var document = JsonDocument.Parse(handler.RequestBody);
        Assert.False(document.RootElement.TryGetProperty("response_format", out _));
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CaptureHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
