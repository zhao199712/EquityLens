using EquityLens.Api.Controllers.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Controllers;

public sealed class SafeExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedFailure_DoesNotExposeInternalAddressOrException()
    {
        var handler = new SafeExceptionHandler(NullLogger<SafeExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        Assert.True(await handler.TryHandleAsync(context, new InvalidOperationException("Failed to connect to http://192.168.1.2:3900/internal"), default));
        body.Position = 0;
        var response = await new StreamReader(body).ReadToEndAsync();
        Assert.Equal(500, context.Response.StatusCode);
        Assert.DoesNotContain("192.168", response);
        Assert.DoesNotContain("InvalidOperationException", response);
        Assert.Contains("internal_error", response);
    }
}
