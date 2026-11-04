using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents;

/// <summary>提前拒絕明確要求存取的未授權投組 ID；不取代建立服務的完整權限檢查。</summary>
internal static class ExplicitPortfolioAccess
{
    // Only a direct access command at a clause boundary; incidental run IDs,
    // quoted examples and negated requests must not become authorization targets.
    private static readonly Regex Request = new(
        @"(?:^|[，。；！？\n])\s*(?:請)?(?:幫我|替我)?(?:先|再|然後|接著|繼續)?(?:分析|診斷|查看|查詢|讀取|存取|優化)\s*(?:別人的?|他人的?|我的?|這個|該)?(?:投資組合|投組)\s*(?:ID\s*[:：]?\s*)?(?<id>[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})(?![0-9a-f-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool RequestsUnownedPortfolio(string message, IEnumerable<Guid> ownedIds)
    {
        var owned = ownedIds.ToHashSet();
        return Request.Matches(message).Any(match => !owned.Contains(Guid.Parse(match.Groups["id"].Value)));
    }
}
