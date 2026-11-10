using EquityLens.Api.Data;
using EquityLens.Api.Services.AdminJobs;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>單一證券的重建結果摘要。</summary>
public sealed record AdjustedCloseRebuildItem(string Ticker, bool Applied, string Reason, int UpdatedCount, int EventCount, IReadOnlyList<string> Issues);

/// <summary>批次重建結果。</summary>
public sealed record AdjustedCloseRebuildJobResult(int Total, int Applied, int Skipped, int UpdatedRows, IReadOnlyList<AdjustedCloseRebuildItem> Items);

/// <summary>
/// 逐檔以官方公司行動重建所有 FinMind 台股的全歷史還原價。
/// </summary>
public interface IAdjustedCloseRebuildJob
{
    /// <summary>
    /// 執行批次重建；每檔之間間隔指定秒數以避免 FinMind 額度用盡。
    /// </summary>
    Task<AdjustedCloseRebuildJobResult> RunAsync(Guid jobRunId, TimeSpan delayBetweenSecurities, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AdjustedCloseRebuildJob(EquityLensDbContext db, IOfficialAdjustedCloseRebuilder rebuilder) : IAdjustedCloseRebuildJob
{
    /// <inheritdoc />
    public async Task<AdjustedCloseRebuildJobResult> RunAsync(Guid jobRunId, TimeSpan delayBetweenSecurities, CancellationToken cancellationToken)
    {
        var securities = await db.Securities
            .Where(x => x.IsActive && (x.Exchange == "TWSE" || x.Exchange == "TPEX") && x.PricesSource == "FinMind")
            .OrderBy(x => x.Ticker)
            .ToListAsync(cancellationToken);
        var items = new List<AdjustedCloseRebuildItem>();
        for (var i = 0; i < securities.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await db.JobRuns.AsNoTracking().AnyAsync(x => x.Id == jobRunId && x.Status == "Cancelled", cancellationToken))
                throw new JobCancelledException();
            if (i > 0 && delayBetweenSecurities > TimeSpan.Zero) await Task.Delay(delayBetweenSecurities, cancellationToken);

            var result = await rebuilder.RebuildAsync(securities[i], cancellationToken);
            items.Add(new(securities[i].Ticker, result.Applied, result.Reason, result.UpdatedCount, result.EventCount, result.Issues.Take(5).ToList()));

            var job = await db.JobRuns.SingleOrDefaultAsync(x => x.Id == jobRunId, cancellationToken);
            if (job is not null)
            {
                job.ProgressPercent = (int)Math.Floor((i + 1) * 100m / securities.Count);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return new(items.Count, items.Count(x => x.Applied), items.Count(x => !x.Applied), items.Sum(x => x.UpdatedCount), items);
    }
}
