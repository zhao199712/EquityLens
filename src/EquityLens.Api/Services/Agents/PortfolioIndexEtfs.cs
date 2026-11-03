using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record PortfolioIndexEtfCandidate(string Ticker, string Name, string Status, string? Reason,
    string Index, string Source);

/// <summary>經明確核對的非槓桿台股指數 ETF 範圍；只從 DB 讀取行情，不接受模型指定行情或任意新標的。</summary>
internal static class PortfolioIndexEtfs
{
    // 核對日期 2026-11-03；擴充清單須另核對指數、幣別與是否槓桿／反向。
    internal static readonly PortfolioIndexEtfCandidate[] Universe = [
        new("0050", "元大台灣50", "Unavailable", null, "臺灣50", "https://www.yuantaetfs.com/product/detail/0050/Basic_information"),
        new("006208", "富邦台50", "Unavailable", null, "臺灣50", "https://websys.fsit.com.tw/FubonETF/Fund/IndexIntro.aspx?stkId=006208")
    ];
    internal static bool IsSupported(string? ticker) => Universe.Any(x => x.Ticker == ticker);
    internal static bool Matches(Security security, string currency) => IsSupported(security.Ticker)
        && security.IsActive && security.Exchange == "TWSE" && security.Currency == "TWD" && currency == "TWD"
        && string.Equals(security.AssetType, "ETF", StringComparison.OrdinalIgnoreCase);

    /// <summary>沿用已保存持股的日期及估值日；候選缺任何一日還原價時拒絕，不縮短共同期間或補值。</summary>
    internal static async Task<PortfolioScenarioSnapshot> AttachAsync(EquityLensDbContext db,
        PortfolioScenarioSnapshot snapshot, CancellationToken ct)
    {
        var tickers = Universe.Select(x => x.Ticker).ToArray();
        var securities = await db.Securities.AsNoTracking().Where(x => tickers.Contains(x.Ticker)).ToListAsync(ct);
        var assets = snapshot.Assets.ToList();
        var candidates = new List<PortfolioIndexEtfCandidate>();
        foreach (var candidate in Universe)
        {
            string? reason = null;
            var matches = securities.Where(x => x.Ticker == candidate.Ticker && Matches(x, snapshot.Currency)).ToArray();
            if (matches.Length != 1) reason = "缺少唯一、啟用、同幣別且確認為 TWSE ETF 的證券資料。";
            else if (snapshot.PriceBasis != "AdjustedClose") reason = "既有持股缺少完整還原收盤價，不能與 ETF 混用價格口徑。";
            else if (snapshot.Dates.Count < 121) reason = "至少需要120筆同期間共同日報酬。";
            else
            {
                var securityId = matches[0].Id;
                var start = DateTime.SpecifyKind(snapshot.Dates[0].ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var end = DateTime.SpecifyKind(snapshot.Dates[^1].ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
                var prices = await db.MarketPrices.AsNoTracking().Where(x => x.SecurityId == securityId && x.Interval == "1d"
                    && x.PriceTime >= start && x.PriceTime <= end).ToListAsync(ct);
                var map = prices.GroupBy(x => DateOnly.FromDateTime(x.PriceTime)).ToDictionary(g => g.Key, g => g.ToArray());
                if (snapshot.Dates.Any(d => !map.TryGetValue(d, out var rows) || rows.Length != 1 || rows[0].Close <= 0 || rows[0].AdjustedClose is not > 0))
                    reason = "缺少完整同期間還原收盤價（或有重複日資料）；不以原始收盤價代替，不縮短期間。";
                else
                {
                    var series = snapshot.Dates.Select(d => map[d][0].AdjustedClose!.Value).ToArray();
                    var existing = assets.FindIndex(a => a.Ticker == candidate.Ticker);
                    if (existing >= 0)
                    {
                        // 既有 ETF 的價序列也必須與此已凍結快照相同，不能在比較中偷換基準。
                        if (!assets[existing].Prices.SequenceEqual(series) || assets[existing].ValuationPrice != map[snapshot.Dates[^1]][0].Close)
                            reason = "ETF 行情與已保存持股快照不一致，請重新取得快照。";
                        else assets[existing] = assets[existing] with { IsEligibleIndexEtf = true };
                    }
                    else assets.Add(new(candidate.Ticker, 0, map[snapshot.Dates[^1]][0].Close, series, candidate.Name, true));
                }
            }
            candidates.Add(candidate with { Status = reason is null ? "Ready" : "Unavailable", Reason = reason });
        }
        return snapshot with { Assets = assets, IndexEtfCandidates = candidates };
    }
}
