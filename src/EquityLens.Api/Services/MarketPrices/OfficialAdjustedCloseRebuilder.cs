using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>全歷史還原價重建結果。</summary>
/// <param name="Applied">是否已寫入；false 時既有數值完全不變。</param>
/// <param name="Reason">未套用原因或摘要。</param>
/// <param name="UpdatedCount">實際更新的列數。</param>
/// <param name="EventCount">納入計算的公司行動數。</param>
/// <param name="Issues">安全檢查發現的問題。</param>
public sealed record OfficialAdjustedCloseRebuildResult(
    bool Applied, string Reason, int UpdatedCount, int EventCount, IReadOnlyList<string> Issues);

/// <summary>
/// 以 FinMind 原始收盤價與官方公司行動重建單一證券的全歷史還原價。
/// </summary>
public interface IOfficialAdjustedCloseRebuilder
{
    /// <summary>
    /// 重建指定證券的全部日線還原價。任何安全檢查失敗時不寫入，由呼叫端決定替代方案。
    /// </summary>
    Task<OfficialAdjustedCloseRebuildResult> RebuildAsync(Security security, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class OfficialAdjustedCloseRebuilder(
    EquityLensDbContext db,
    ICorporateActionProvider corporateActions,
    ILogger<OfficialAdjustedCloseRebuilder> logger) : IOfficialAdjustedCloseRebuilder
{
    private const string DailyInterval = "1d";
    private const string RawPriceSource = "FinMind";
    // numeric(18,6) 儲存精度內視為相同，不重寫。
    private const decimal UnchangedTolerance = 0.0000005m;

    /// <inheritdoc />
    public async Task<OfficialAdjustedCloseRebuildResult> RebuildAsync(Security security, CancellationToken cancellationToken)
    {
        if (security.Exchange is not ("TWSE" or "TPEX"))
            return Skip(security, "unsupported_exchange");

        var rows = await db.MarketPrices
            .Where(x => x.SecurityId == security.Id && x.Interval == DailyInterval)
            .OrderBy(x => x.PriceTime)
            .ToListAsync(cancellationToken);
        if (rows.Count < 2) return Skip(security, "insufficient_prices");
        // 公司行動參考價對應交易所實際成交價；混入其他來源（例如已調整分割的收盤價）會產生錯誤因子。
        if (rows.Any(x => x.DataSource != RawPriceSource)) return Skip(security, "mixed_price_sources");

        var first = DateOnly.FromDateTime(rows[0].PriceTime);
        var last = DateOnly.FromDateTime(rows[^1].PriceTime);
        IReadOnlyList<CorporateAction> actions;
        try
        {
            actions = await corporateActions.GetCorporateActionsAsync(security.Ticker, first, last, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException)
        {
            logger.LogWarning(ex, "Corporate actions unavailable for {Ticker}; adjusted close not rebuilt.", security.Ticker);
            return Skip(security, "corporate_actions_unavailable");
        }

        var calculation = OfficialAdjustedCloseCalculator.Calculate(
            rows.Select(x => (DateOnly.FromDateTime(x.PriceTime), x.Close)).ToList(), actions);
        if (!calculation.Applied)
        {
            logger.LogWarning("Official adjusted close rejected for {Ticker}: {Issues}", security.Ticker, string.Join("; ", calculation.Issues));
            return new(false, "validation_failed", 0, calculation.EventCount, calculation.Issues);
        }

        var now = DateTime.UtcNow;
        var updated = 0;
        foreach (var row in rows)
        {
            if (!calculation.AdjustedCloseByDate.TryGetValue(DateOnly.FromDateTime(row.PriceTime), out var value)) continue;
            if (row.AdjustedClose is { } existing && Math.Abs(existing - value) <= UnchangedTolerance) continue;
            row.AdjustedClose = value;
            row.UpdatedAtUtc = now;
            updated++;
        }
        if (updated > 0) await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Rebuilt official adjusted close for {Ticker}: {Updated} row(s), {Events} corporate action(s), {NonTrading} non-trading day(s) carried forward.",
            security.Ticker, updated, calculation.EventCount, calculation.NonTradingDays);
        return new(true, "applied", updated, calculation.EventCount, []);
    }

    private static OfficialAdjustedCloseRebuildResult Skip(Security security, string reason) => new(false, reason, 0, 0, []);
}
