using System.Net;
using EquityLens.Api.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Chat;

public sealed class BraveSearchServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, WebProviderErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, WebProviderErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, WebProviderErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, WebProviderErrorCodes.ProviderError)]
    public async Task Search_NonSuccess_ThrowsClassifiedProviderError(HttpStatusCode status, string expectedCode)
    {
        var service = Create((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("provider detail must not leak") }));
        var error = await Assert.ThrowsAsync<WebProviderException>(() => service.SearchAsync("query"));
        Assert.Equal(expectedCode, error.ErrorCode); Assert.Equal((int)status, error.HttpStatusCode); Assert.DoesNotContain("provider detail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_NetworkFailure_ThrowsNetworkError()
    {
        var service = Create((_, _) => throw new HttpRequestException("dns failed"));
        var error = await Assert.ThrowsAsync<WebProviderException>(() => service.SearchAsync("query"));
        Assert.Equal(WebProviderErrorCodes.NetworkError, error.ErrorCode);
    }

    [Fact]
    public async Task Search_InternalTimeout_ThrowsTransientTimeout()
    {
        var service = Create((_, _) => throw new TaskCanceledException("timeout"));
        var error = await Assert.ThrowsAsync<WebProviderException>(() => service.SearchAsync("query"));
        Assert.Equal(WebProviderErrorCodes.Timeout, error.ErrorCode); Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task Search_CallerCancellation_IsNotConvertedToProviderFailure()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = Create((_, token) => throw new OperationCanceledException(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("query", cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData(WebProviderErrorCodes.NetworkError, null, true)]
    [InlineData(WebProviderErrorCodes.RateLimited, 429, true)]
    [InlineData(WebProviderErrorCodes.ProviderError, 500, true)]
    [InlineData(WebProviderErrorCodes.InvalidResponse, null, true)]
    [InlineData(WebProviderErrorCodes.Configuration, null, false)]
    [InlineData(WebProviderErrorCodes.Unauthorized, 401, false)]
    [InlineData(WebProviderErrorCodes.ProviderError, 400, false)]
    public void ProviderError_Classification_IsExplicit(string code, int? status, bool expected)
    {
        Assert.Equal(expected, new WebProviderException("test", code, status).IsTransient);
    }

    [Fact]
    public async Task Search_ValidEmptyResponse_IsRealNoResults()
    {
        var service = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"web\":{\"results\":[]}}") }));
        var response = await service.SearchAsync("query");
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Search_MapsNeutralFreshnessToBraveValue()
    {
        Uri? requested = null; var service = Create((request, _) => { requested = request.RequestUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"web\":{\"results\":[]}}") }); });
        await service.SearchAsync("query", freshness: "year");
        Assert.Contains("freshness=py", requested!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_MissingKey_ThrowsConfigurationErrorWithoutSending()
    {
        var sent = false; var service = Create((_, _) => { sent = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }, apiKey: "");
        var error = await Assert.ThrowsAsync<WebProviderException>(() => service.SearchAsync("query"));
        Assert.Equal(WebProviderErrorCodes.Configuration, error.ErrorCode); Assert.False(sent);
    }

    private static BraveSearchService Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response, string apiKey = "secret") =>
        new(new HttpClient(new StubHandler(response)), Options.Create(new BraveOptions { ApiKey = apiKey }), NullLogger<BraveSearchService>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}
