using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using EquityLens.Api.Controllers.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace EquityLens.Api.Common;

/// <summary>
/// 呼叫付費 LLM／檢索 API 端點的速率限制設定（設定區段 <c>RateLimiting:Llm</c>）。
/// </summary>
public sealed class LlmRateLimitOptions
{
    public const string SectionName = "RateLimiting:Llm";

    /// <summary>每個一般使用者在一個時間窗內允許的請求數。</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>管理員在一個時間窗內允許的請求數；給較高額度而非無上限，避免 token 外洩時費用失控。</summary>
    public int AdminPermitLimit { get; set; } = 120;

    /// <summary>全站所有使用者合計在一個時間窗內允許的請求數；開放註冊時，擋住大量帳號各自用滿額度。</summary>
    public int GlobalPermitLimit { get; set; } = 60;

    /// <summary>時間窗長度（秒）。</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// 登入與註冊端點的速率限制設定（設定區段 <c>RateLimiting:Auth</c>），以來源 IP 分區。
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    /// <summary>每個 IP 在一個時間窗內允許的登入／換發 token 次數。</summary>
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>每個 IP 在註冊時間窗內允許建立的帳號數。</summary>
    public int RegisterPermitLimit { get; set; } = 3;

    public int RegisterWindowSeconds { get; set; } = 3600;
}

/// <summary>
/// 速率限制政策。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Llm"/>：會呼叫 DeepSeek、Cohere、Brave 等付費 API 的端點，依使用者分區；
/// 另有一個全站共用的上限（<see cref="LlmRateLimitOptions.GlobalPermitLimit"/>），因為註冊是開放的，
/// 單靠每使用者額度會被「多開帳號」繞過。</item>
/// <item><see cref="Auth"/>／<see cref="Register"/>：登入與註冊依來源 IP 分區，
/// 需要 <see cref="ForwardedHeadersSetup"/> 才能拿到 Caddy 後面的真實 IP。</item>
/// </list>
/// </remarks>
public static class RateLimitPolicies
{
    public const string Llm = "llm";
    public const string Auth = "auth";
    public const string Register = "register";

    public static IServiceCollection AddEquityLensRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var llm = configuration.GetSection(LlmRateLimitOptions.SectionName).Get<LlmRateLimitOptions>() ?? new LlmRateLimitOptions();
        var auth = configuration.GetSection(AuthRateLimitOptions.SectionName).Get<AuthRateLimitOptions>() ?? new AuthRateLimitOptions();
        services.Configure<ActiveAgentRunLimitOptions>(configuration.GetSection(ActiveAgentRunLimitOptions.SectionName));

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

            // 全站 LLM 總量：GlobalLimiter 會套用在每個請求上，這裡只對標記 Llm 政策的端點計數。
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => CreateGlobalPartition(context, llm));
            limiter.AddPolicy(Llm, context => CreateLlmPartition(context.User, llm));
            limiter.AddPolicy(Auth, context => CreateIpPartition(context, "auth", auth.PermitLimit, auth.WindowSeconds));
            limiter.AddPolicy(Register, context => CreateIpPartition(context, "register", auth.RegisterPermitLimit, auth.RegisterWindowSeconds));
        });

        return services;
    }

    /// <summary>
    /// 依使用者建立分區：管理員使用較高額度，其餘使用者各自一個固定時間窗限制器。
    /// </summary>
    internal static RateLimitPartition<string> CreateLlmPartition(ClaimsPrincipal user, LlmRateLimitOptions options)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var isAdmin = user.IsInRole("Admin");
        return FixedWindow(isAdmin ? $"admin:{userId}" : $"user:{userId}",
            isAdmin ? options.AdminPermitLimit : options.PermitLimit,
            options.WindowSeconds);
    }

    /// <summary>
    /// 全站分區：只有標記 <see cref="Llm"/> 政策的端點共用同一個計數器，其他端點不受影響。
    /// </summary>
    internal static RateLimitPartition<string> CreateGlobalPartition(HttpContext context, LlmRateLimitOptions options)
    {
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        return string.Equals(policy, Llm, StringComparison.Ordinal)
            ? FixedWindow("global:llm", options.GlobalPermitLimit, options.WindowSeconds)
            : RateLimitPartition.GetNoLimiter("global:none");
    }

    internal static RateLimitPartition<string> CreateIpPartition(HttpContext context, string scope, int permitLimit, int windowSeconds)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return FixedWindow($"{scope}:{ip}", permitLimit, windowSeconds);
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
