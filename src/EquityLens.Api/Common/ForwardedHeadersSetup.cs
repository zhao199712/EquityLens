using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace EquityLens.Api.Common;

/// <summary>
/// 讓 API 在 Caddy 反向代理後面取得使用者的真實 IP（依 IP 限流需要它）。
/// </summary>
/// <remarks>
/// 正式環境中 API 沒有對外開 port，只有同一個 Docker 網路裡的 Caddy 連得到它，
/// 所以只信任私有網段送來的 <c>X-Forwarded-For</c>。<see cref="ForwardedHeadersOptions.ForwardLimit"/> 設為 1，
/// 只採用最右邊、由 Caddy 附加的那一個位址；使用者自己偽造的標頭值會落在更左邊而被忽略。
/// </remarks>
public static class ForwardedHeadersSetup
{
    private static readonly (string Prefix, int Length)[] TrustedPrivateNetworks =
    [
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16),
        ("127.0.0.0", 8),
    ];

    public static IServiceCollection AddEquityLensForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
#pragma warning disable CS0618, ASPDEPR005 // KnownNetworks is superseded in newer ASP.NET versions but still honoured
            options.KnownNetworks.Clear();
            foreach (var (prefix, length) in TrustedPrivateNetworks)
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(prefix), length));
            }
#pragma warning restore CS0618, ASPDEPR005
        });

        return services;
    }
}
