using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Chat;

public sealed class CohereRerankServiceTests
{
    [Fact]
    public async Task RerankAsync_HttpClientTimeout_ReturnsTimeoutDiagnostics()
    {
        using var client = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { Timeout = TimeSpan.FromMilliseconds(100) };
        var service = CreateService(client);

        var result = await service.RerankAsync("query", ["document"], 1);

        Assert.Equal("TimedOut", result.Status);
        Assert.Equal("CohereTimeout", result.FallbackReason);
        Assert.Equal(1, result.CandidateCount);
        Assert.True(result.PayloadBytes > 0);
        Assert.True(result.DurationMs >= 50);
        Assert.Null(result.HttpStatusCode);
    }

    [Fact]
    public async Task RerankAsync_ExternalCancellation_PropagatesCancellation()
    {
        using var client = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { Timeout = TimeSpan.FromSeconds(10) };
        var service = CreateService(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RerankAsync("query", ["document"], 1, cancellation.Token));
    }

    [Fact]
    public async Task RerankAsync_HttpFailure_RecordsStatusWithoutResponseBody()
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("sensitive provider response")
            })));
        var service = CreateService(client);

        var result = await service.RerankAsync("query", ["document"], 1);

        Assert.Equal("HttpError", result.Status);
        Assert.Equal("CohereHttp429", result.FallbackReason);
        Assert.Equal(429, result.HttpStatusCode);
        Assert.DoesNotContain("sensitive", result.FallbackReason, StringComparison.OrdinalIgnoreCase);
    }

    private static CohereRerankService CreateService(HttpClient client) => new(
        client,
        Options.Create(new CohereOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://cohere.test/v2",
            RerankModel = "test-model"
        }),
        NullLogger<CohereRerankService>.Instance);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
