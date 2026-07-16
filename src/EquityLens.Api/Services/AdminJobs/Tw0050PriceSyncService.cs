using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.AdminJobs;

public interface ITw0050PriceSyncService
{
    Task<Tw0050PriceSyncResponse> SyncAsync(Guid jobRunId, CancellationToken cancellationToken = default);
}

public sealed class JobCancelledException : Exception
{
    public JobCancelledException() : base("工作已取消。") { }
}

public sealed record Tw0050PriceSyncResponse(
    DateOnly? SourceAsOfDate,
    int ConstituentCount,
    int CreatedSecurityCount,
    int SyncedCount,
    int SkippedCount,
    int FailedCount,
    int ImportedPriceCount,
    IReadOnlyList<Tw0050PriceSyncItemResponse> Items);

public sealed record Tw0050PriceSyncItemResponse(
    string Ticker,
    string Name,
    DateOnly? From,
    DateOnly? To,
    string Status,
    int ImportedPriceCount,
    string? Reason);

/// <summary>
/// 將目前元大0050股票成分的缺失日價格補至最新可得交易日。
/// </summary>
public sealed class Tw0050PriceSyncService : ITw0050PriceSyncService
{
    private const string DailyInterval = "1d";
    private readonly EquityLensDbContext _dbContext;
    private readonly IMarketPriceService _marketPriceService;

    public Tw0050PriceSyncService(
        EquityLensDbContext dbContext,
        IMarketPriceService marketPriceService)
    {
        _dbContext = dbContext;
        _marketPriceService = marketPriceService;
    }

    public async Task<Tw0050PriceSyncResponse> SyncAsync(Guid jobRunId, CancellationToken cancellationToken = default)
    {
        var snapshot = Tw0050ConstituentCatalog.Current;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = new List<Tw0050PriceSyncItemResponse>();
        var created = 0;
        var synced = 0;
        var skipped = 0;
        var failed = 0;
        var imported = 0;
        var completed = 0;

        foreach (var constituent in snapshot.Constituents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isCancelled = await _dbContext.JobRuns.AsNoTracking()
                .AnyAsync(x => x.Id == jobRunId && x.Status == "Cancelled", cancellationToken);
            if (isCancelled)
            {
                throw new JobCancelledException();
            }

            var security = await _dbContext.Securities.SingleOrDefaultAsync(
                x => x.Ticker == constituent.StockCode && x.Exchange == "TWSE" && x.IsActive,
                cancellationToken);
            if (security is null)
            {
                security = new Security
                {
                    Id = Guid.NewGuid(),
                    Ticker = constituent.StockCode,
                    Exchange = "TWSE",
                    Name = constituent.Name,
                    AssetType = "Stock",
                    Currency = "TWD",
                    IsActive = true,
                    MetadataSource = "Yuanta0050",
                    MetadataUpdatedAtUtc = DateTime.UtcNow
                };
                _dbContext.Securities.Add(security);
                await _dbContext.SaveChangesAsync(cancellationToken);
                created++;
            }

            var lastPrice = await _dbContext.MarketPrices
                .Where(x => x.SecurityId == security.Id && x.Interval == DailyInterval)
                .OrderByDescending(x => x.PriceTime)
                .Select(x => (DateTime?)x.PriceTime)
                .FirstOrDefaultAsync(cancellationToken);
            var from = lastPrice.HasValue
                ? DateOnly.FromDateTime(lastPrice.Value).AddDays(1)
                : today.AddYears(-3);

            if (from > today)
            {
                skipped++;
                items.Add(new Tw0050PriceSyncItemResponse(
                    constituent.StockCode, constituent.Name, from, today, "skipped", 0, "沒有新的日期需要同步。"));
                await UpdateProgressAsync(jobRunId, ++completed, snapshot.Constituents.Count, cancellationToken);
                continue;
            }

            try
            {
                var result = await _marketPriceService.ImportDailyPricesByTickerAsync(
                    new ImportMarketPricesByTickerRequest(
                        constituent.StockCode, "TWSE", from, today,
                        constituent.Name, "Stock", "TWD", null, null, null),
                    cancellationToken);
                if (result.IsSuccess)
                {
                    var count = result.Value?.ImportedCount ?? 0;
                    synced++;
                    imported += count;
                    items.Add(new Tw0050PriceSyncItemResponse(
                        constituent.StockCode, constituent.Name, from, today, "synced", count, null));
                }
                else
                {
                    failed++;
                    items.Add(new Tw0050PriceSyncItemResponse(
                        constituent.StockCode, constituent.Name, from, today, "failed", 0,
                        $"{result.ErrorCode}: {result.ErrorMessage}"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                items.Add(new Tw0050PriceSyncItemResponse(
                    constituent.StockCode, constituent.Name, from, today, "failed", 0, ex.Message));
            }

            await UpdateProgressAsync(jobRunId, ++completed, snapshot.Constituents.Count, cancellationToken);

            // Provider 可能限流；逐檔節流且保留 CancellationToken 支援。
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return new Tw0050PriceSyncResponse(
            snapshot.SourceAsOfDate,
            snapshot.Constituents.Count,
            created,
            synced,
            skipped,
            failed,
            imported,
            items);
    }

    private async Task UpdateProgressAsync(
        Guid jobRunId,
        int completed,
        int total,
        CancellationToken cancellationToken)
    {
        var jobRun = await _dbContext.JobRuns.SingleOrDefaultAsync(x => x.Id == jobRunId, cancellationToken);
        if (jobRun is null) return;
        jobRun.ProgressPercent = total == 0 ? 0 : (int)Math.Floor(completed * 100m / total);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// 暫時固定的臺灣50股票池，為 2026-06 定期調整生效後的 50 檔。
/// 此 catalog 僅作為資料庫 seed；不會在背景工作呼叫外部成分資料端點。
/// 下一次指數調整時必須以正式公告更新此清單。
/// </summary>
internal static class Tw0050ConstituentCatalog
{
    public static readonly YuantaTw0050ConstituentSnapshot Current = new(
        new DateOnly(2026, 6, 22),
        [
            new("1216", "統一"), new("1303", "南亞"), new("2059", "川湖"),
            new("2301", "光寶科"), new("2303", "聯電"), new("2308", "台達電"),
            new("2317", "鴻海"), new("2327", "國巨"), new("2330", "台積電"),
            new("2344", "華邦電"), new("2345", "智邦"), new("2357", "華碩"),
            new("2360", "致茂"), new("2368", "金像電"), new("2382", "廣達"),
            new("2383", "台光電"), new("2395", "研華"), new("2408", "南亞科"),
            new("2412", "中華電"), new("2449", "京元電子"), new("2454", "聯發科"),
            new("2603", "長榮"), new("2880", "華南金"), new("2881", "富邦金"),
            new("2882", "國泰金"), new("2883", "凱基金"), new("2884", "玉山金"),
            new("2885", "元大金"), new("2886", "兆豐金"), new("2887", "台新新光金"),
            new("2890", "永豐金"), new("2891", "中信金"), new("2892", "第一金"),
            new("3008", "大立光"), new("3017", "奇鋐"), new("3037", "欣興"),
            new("3045", "台灣大"), new("3231", "緯創"), new("3443", "創意"),
            new("3653", "健策"), new("3661", "世芯-KY"), new("3665", "貿聯-KY"),
            new("3711", "日月光投控"), new("4958", "臻鼎-KY"), new("4904", "遠傳"),
            new("5880", "合庫金"), new("6505", "台塑化"), new("6669", "緯穎"),
            new("7769", "鴻勁"), new("8046", "南電")
        ]);
}
