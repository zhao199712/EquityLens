using Microsoft.AspNetCore.Diagnostics;

namespace EquityLens.Api.Controllers.Filters;

/// <summary>非預期錯誤只回傳通用訊息與追蹤識別碼，細節留在伺服器日誌。</summary>
public sealed class SafeExceptionHandler(ILogger<SafeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
        await context.Response.WriteAsJsonAsync(new
        {
            code = context.Response.StatusCode < 500 ? "invalid_request" : "internal_error",
            message = context.Response.StatusCode < 500 ? "請求格式或大小不符合要求。" : "服務暫時無法完成請求，請稍後再試。",
            traceId = context.TraceIdentifier
        }, cancellationToken);
        return true;
    }
}
