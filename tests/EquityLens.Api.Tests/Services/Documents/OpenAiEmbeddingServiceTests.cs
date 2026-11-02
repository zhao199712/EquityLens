using System.Net;
using System.Text;
using EquityLens.Api.Services.Documents;
using Microsoft.Extensions.Configuration;

namespace EquityLens.Api.Tests.Services.Documents;

public sealed class OpenAiEmbeddingServiceTests
{
    [Fact]
    public async Task CreateEmbeddings_UsesConfiguredApiKey()
    {
        var handler = new CapturingHandler();
        var service = new OpenAiEmbeddingService(new HttpClient(handler), BuildConfiguration("sk-test"));

        var embeddings = await service.CreateEmbeddingsAsync(["hello"]);

        Assert.Single(embeddings);
        Assert.Equal("Bearer sk-test", handler.Authorization);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateEmbeddings_BlankApiKey_ThrowsNotConfiguredWithoutCallingOpenAi(string configuredKey)
    {
        // 測試機若設定了 OPENAI_API_KEY，空白設定會正確退回環境變數，此情境不適用
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            return;

        var handler = new CapturingHandler();
        var service = new OpenAiEmbeddingService(new HttpClient(handler), BuildConfiguration(configuredKey));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEmbeddingsAsync(["hello"]));

        Assert.Contains("not configured", exception.Message);
        Assert.Null(handler.Authorization);
    }

    private static IConfiguration BuildConfiguration(string apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = apiKey })
            .Build();

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"index":0,"embedding":[0.1,0.2]}]}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
