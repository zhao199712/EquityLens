using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace EquityLens.Api.Common;

/// <summary>
/// 呼叫付費 LLM／檢索 API 端點的速率限制設定（設定區段 <c>RateLimiting:Llm</c>）。
/// </summary>
public sealed class LlmRateLimitOptions
{
    public const string SectionName = "RateLimiting:Llm";

    /// <summary>每個使用者在一個時間窗內允許的請求數。</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>時間窗長度（秒）。</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// 速率限制政策。會呼叫 DeepSeek、Cohere、Brave 等付費 API 的端點套用 <see cref="Llm"/>，
/// 依登入使用者分區，避免單一帳號以迴圈大量呼叫造成費用失控。管理員不受限制（例如執行 eval）。
/// </summary>
public static class RateLimitPolicies
{
    public const string Llm = "llm";

    public static IServiceCollection AddEquityLensRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(LlmRateLimitOptions.SectionName).Get<LlmRateLimitOptions>() ?? new LlmRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError("rate_limited", "請求過於頻繁，請稍後再試。"),
                    cancellationToken);
            };
            limiter.AddPolicy(Llm, httpContext => CreateLlmPartition(httpContext.User, options));
        });

        return services;
    }

    /// <summary>
    /// 依使用者建立分區：管理員不限制，其餘使用者各自一個固定時間窗限制器。
    /// </summary>
    internal static RateLimitPartition<string> CreateLlmPartition(ClaimsPrincipal user, LlmRateLimitOptions options)
    {
        if (user.IsInRole("Admin"))
        {
            return RateLimitPartition.GetNoLimiter("admin");
        }

        // 端點都要求登入，理論上一定有使用者識別碼；保險起見，缺少時全部共用一個最嚴格的分區。
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
