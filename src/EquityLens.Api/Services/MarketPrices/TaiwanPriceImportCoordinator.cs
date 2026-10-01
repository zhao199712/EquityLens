using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>台股共用匯入閘門；擷取不持有交易，提交使用獨立工作單元。</summary>
public sealed class TaiwanPriceImportCoordinator(
    IDbContextFactory<EquityLensDbContext> contexts,
    IEnumerable<IMarketDataProvider> providers,
    ITaiwanPriceEvidenceProvider evidenceProvider)
{
    public async Task<Result<ImportMarketPricesResponse>> ImportAsync(Guid securityId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        Security security;
        List<MarketPrice> existing;
        PriceAdjustmentBatch? currentBatch;
        await using (var read = await contexts.CreateDbContextAsync(ct))
        {
            security = await read.Securities.AsNoTracking().SingleAsync(x => x.Id == securityId, ct);
            existing = await read.MarketPrices.AsNoTracking().Where(x => x.SecurityId == securityId && x.Interval == "1d")
                .OrderBy(x => x.PriceTime).ToListAsync(ct);
            currentBatch = security.PriceAdjustmentBatchId is null ? null :
                await read.PriceAdjustmentBatches.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Id == security.PriceAdjustmentBatchId && x.SecurityId == securityId, ct);
        }
        var fingerprint = Fingerprint(security, existing);
        var fullFrom = existing.Count == 0 ? from : DateOnly.FromDateTime(existing[0].PriceTime);
        if (from < fullFrom) fullFrom = from;
        var fullTo = existing.Count == 0 ? to : DateOnly.FromDateTime(existing[^1].PriceTime);
        if (to > fullTo) fullTo = to;
        // 端點價格被刪除時，仍保留上一個驗證批次的範圍並重新擷取整段。
        if (currentBatch is not null)
        {
            if (currentBatch.From < fullFrom) fullFrom = currentBatch.From;
            if (currentBatch.To > fullTo) fullTo = currentBatch.To;
        }
        var finmind = providers.FirstOrDefault(x => x.SourceName == "FinMind");
        if (finmind is null) return Failure("provider_error", "FinMind price provider is unavailable.");

        var committing = false;
        try
        {
            // 全歷史 raw 與 adjusted close 使用同一更新範圍；舊 adjusted close 絕不補洞。
            var raw = await finmind.GetDailyPricesAsync(security, fullFrom, fullTo, ct);
            EnsureUnique(raw);
            if (raw.Count == 0) throw new PriceIntegrityException("Source returned no prices.");
            if (raw.Any(x => x.Date < fullFrom || x.Date > fullTo))
                throw new PriceIntegrityException("Raw source returned an out-of-range date.");
            var yahoo = providers.OfType<IAdjustedCloseProvider>().FirstOrDefault();
            AdjustmentSnapshot? adjustment = null;
            string? yahooFailure = null;
            if (yahoo is not null)
            {
                try { adjustment = await yahoo.GetAdjustmentSnapshotAsync(security, fullFrom, fullTo, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { yahooFailure = "timeout"; }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                { yahooFailure = ex.GetType().Name; }
            }
            var incomplete = adjustment is null || raw.Any(x => !adjustment.AdjustedCloses.ContainsKey(x.Date));
            var evidence = await evidenceProvider.GetEvidenceAsync(security, fullFrom, fullTo, raw, incomplete, ct);
            var byDate = raw.ToDictionary(x => x.Date);
            foreach (var correction in evidence.Corrections) byDate[correction.Key] = correction.Value;
            foreach (var date in evidence.ConfirmedNoTradeDates)
            {
                if (byDate.TryGetValue(date, out var value) && value.Close > 0)
                    throw new PriceIntegrityException($"No-trade evidence conflicts with a positive close on {date}.");
                byDate.Remove(date);
            }
            raw = byDate.Values.OrderBy(x => x.Date).ToList();
            if (raw.Any(x => !evidence.TradingDates.Contains(x.Date)))
                throw new PriceIntegrityException("Price date is outside the verified complete trading calendar.");
            if (raw.Any(x => adjustment is null || !adjustment.AdjustedCloses.ContainsKey(x.Date)) && !evidence.ActionsComplete)
                evidence = await evidenceProvider.GetEvidenceAsync(security, fullFrom, fullTo, raw, true, ct);
            if (raw.Count == 0) throw new PriceIntegrityException("No valid trading observations.");
            foreach (var p in raw)
                if (p.Open <= 0 || p.High <= 0 || p.Low <= 0 || p.Close <= 0 ||
                    p.High < p.Low || p.High < p.Open || p.High < p.Close || p.Low > p.Open || p.Low > p.Close ||
                    p.Volume < 0)
                    throw new PriceIntegrityException($"Invalid OHLC/volume on {p.Date}.");
            // 已有交易日不能被來源遺漏；有官方無成交證據才能移除。
            foreach (var p in existing)
            {
                var date = DateOnly.FromDateTime(p.PriceTime);
                if (!byDate.ContainsKey(date) && !evidence.ConfirmedNoTradeDates.Contains(date))
                    throw new PriceIntegrityException($"Existing trading observation missing from new source: {date}.");
            }
            foreach (var date in evidence.TradingDates.Where(x => x >= fullFrom && x <= fullTo))
                if (!byDate.ContainsKey(date) && !evidence.ConfirmedNoTradeDates.Contains(date))
                    throw new PriceIntegrityException($"Unverified missing trading day: {date}.");
            var expectedEnd = evidence.TradingDates.Where(x => x <= to).Select(x => (DateOnly?)x).Max();
            if (expectedEnd is null || expectedEnd < from)
                throw new PriceIntegrityException("Requested range has no confirmed complete trading day.");
            if (raw.Max(x => x.Date) < expectedEnd && !evidence.ConfirmedNoTradeDates.Contains(expectedEnd.Value))
                throw new PriceIntegrityException($"Source does not cover cutoff {expectedEnd}.");
            IReadOnlyDictionary<DateOnly, decimal> adjusted;
            var source = "YahooFinance";
            if (adjustment is not null && raw.All(x => adjustment.AdjustedCloses.ContainsKey(x.Date)))
                adjusted = adjustment.AdjustedCloses;
            else
            {
                if (!evidence.ActionsComplete) throw new PriceIntegrityException("Corporate-action coverage cannot be verified.");
                if (adjustment is { ActionsComplete: true })
                    ReconcileActions(raw, adjustment.Actions, evidence.Actions);
                adjusted = PriceAdjustmentChain.Rebuild(raw, evidence.Actions);
                source = security.Exchange + "+FinMindOfficial";
            }
            if (raw.Any(x => !adjusted.TryGetValue(x.Date, out var value) || decimal.Round(value, 6) <= 0))
                throw new PriceIntegrityException("Complete positive adjusted-close series is required.");

            var series = raw.Select(x => x with { AdjustedClose = decimal.Round(adjusted[x.Date], 6, MidpointRounding.ToEven) }).ToList();
            var json = JsonSerializer.Serialize(new
            {
                ticker = security.Ticker, exchange = security.Exchange, from = fullFrom, to = fullTo,
                raw = finmind is IMarketDataEvidenceProvider rawEvidence ? rawEvidence.LastPriceEvidenceJson : JsonSerializer.Serialize(raw),
                yahoo = adjustment?.EvidenceJson, yahooFailure, official = evidence.EvidenceJson,
                noTrades = evidence.ConfirmedNoTradeDates.Order().ToArray(),
                sourceProvenance = (finmind as IMarketDataEvidenceProvider)?.SourceProvenanceJson
            });
            var batch = new PriceAdjustmentBatch
            {
                Id = Guid.NewGuid(), SecurityId = securityId, FetchedAtUtc = DateTime.UtcNow,
                From = fullFrom, To = fullTo, VerifiedThrough = expectedEnd.Value,
                Source = source, AlgorithmVersion = PriceAdjustmentChain.Version,
                SnapshotJson = json, SnapshotHash = Hash(json), SeriesHash = Hash(JsonSerializer.Serialize(series))
            };
            committing = true;
            return await CommitAsync(securityId, fingerprint, series, evidence.ConfirmedNoTradeDates, batch, from, to, ct);
        }
        catch (PriceIntegrityException ex) { return Failure("quality_error", ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !committing)
        { return Failure("provider_error", "Source request timed out; no prices were committed."); }
        catch (Exception ex) when (!committing && ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        { return Failure("provider_error", $"Source failure ({ex.GetType().Name}{(ex is HttpRequestException h ? ", HTTP " + h.StatusCode : "")}); no prices were committed."); }
    }

    private static Result<ImportMarketPricesResponse> Failure(string code, string message) =>
        Result<ImportMarketPricesResponse>.Failure("market_price." + code, message);

    public async Task<Result<ImportMarketPricesResponse>> CommitAsync(Guid id, string expectedFingerprint,
        IReadOnlyList<ImportedMarketPrice> series, IReadOnlySet<DateOnly> noTrades,
        PriceAdjustmentBatch batch, DateOnly requestedFrom, DateOnly requestedTo, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("Atomic Taiwan imports require a relational database.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // 以固定順序鎖定證券與既有價格，其他匯入者必須等待本交易完成。
            var locked = await db.Securities.FromSqlInterpolated($"SELECT * FROM security WHERE id = {id} FOR UPDATE").ToListAsync(ct);
            if (locked.Count == 0) return Failure("update_conflict", "Security was removed during source fetching.");
            var security = locked[0];
            var rows = await db.MarketPrices.Where(x => x.SecurityId == id && x.Interval == "1d").OrderBy(x => x.PriceTime).ToListAsync(ct);
            if (Fingerprint(security, rows) != expectedFingerprint)
                return Failure("update_conflict", "Prices changed during source fetching; refetch before retrying.");
            var map = rows.ToDictionary(x => DateOnly.FromDateTime(x.PriceTime));
            db.PriceAdjustmentBatches.Add(batch);
            var inserted = 0;
            var updated = 0;
            var now = DateTime.UtcNow;
            foreach (var price in series)
            {
                if (!map.TryGetValue(price.Date, out var row))
                {
                    row = new MarketPrice { Id = Guid.NewGuid(), SecurityId = id,
                        PriceTime = price.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Interval = "1d" };
                    db.MarketPrices.Add(row);
                    if (price.Date >= requestedFrom && price.Date <= requestedTo) inserted++;
                }
                else if (price.Date >= requestedFrom && price.Date <= requestedTo) updated++;
                row.Open = price.Open; row.High = price.High; row.Low = price.Low; row.Close = price.Close;
                row.AdjustedClose = price.AdjustedClose; row.Volume = price.Volume;
                row.DataSource = price.RawSource ?? "FinMind"; row.UpdatedAtUtc = now; row.PriceAdjustmentBatchId = batch.Id;
            }
            foreach (var row in rows.Where(x => noTrades.Contains(DateOnly.FromDateTime(x.PriceTime))))
                db.MarketPrices.Remove(row);
            security.PriceAdjustmentBatchId = batch.Id;
            security.PricesVerifiedThrough = batch.VerifiedThrough;
            security.PricesSyncedAtUtc = now; security.PricesSource = "FinMind";
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<ImportMarketPricesResponse>.Success(new(id, "FinMind", inserted + updated, inserted, updated,
                batch.Source, batch.Id, batch.From, batch.To, batch.VerifiedThrough));
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failure("update_conflict", "Concurrent price update; refetch before retrying.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private static bool IsConcurrencyConflict(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or PostgresException { SqlState: "40001" or "40P01" })
                return true;
        return false;
    }

    public static string Fingerprint(Security s, IReadOnlyList<MarketPrice> rows) => Hash(JsonSerializer.Serialize(new
    {
        s.Id, s.Ticker, s.Exchange, s.IsActive, s.PriceAdjustmentBatchId, s.PricesVerifiedThrough, s.PricesSyncedAtUtc, s.PricesSource,
        prices = rows.OrderBy(x => x.PriceTime).Select(x => new
        { x.Id, x.PriceTime, x.Open, x.High, x.Low, x.Close, x.AdjustedClose, x.Volume, x.DataSource,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.PriceAdjustmentBatchId })
    }));
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureUnique(IReadOnlyList<ImportedMarketPrice> prices)
    {
        if (prices.GroupBy(x => x.Date).Any(x => x.Count() != 1))
            throw new PriceIntegrityException("Duplicate raw-price dates.");
    }

    private static void ReconcileActions(IReadOnlyList<ImportedMarketPrice> raw,
        IReadOnlyList<CorporatePriceAction> yahoo, IReadOnlyList<CorporatePriceAction> official)
    {
        var yahooDates = yahoo.Select(x => x.Date).ToHashSet();
        foreach (var group in yahoo.GroupBy(x => x.Date))
        {
            var matches = official.Where(x => x.Date == group.Key).ToList();
            if (matches.Count != 1)
                throw new PriceIntegrityException($"Unmatched/ambiguous action on {group.Key:yyyy-MM-dd}.");
            var prior = raw.Where(x => x.Date < group.Key).OrderBy(x => x.Date).LastOrDefault();
            if (prior is null) continue; // 區間首日事件不影響此區間內的歷史價格。
            var yahooValue = PriceAdjustmentChain.Rebuild(raw, group.ToList())[prior.Date];
            var officialValue = PriceAdjustmentChain.Rebuild(raw, matches)[prior.Date];
            if (Math.Abs(yahooValue - officialValue) / officialValue > 0.001m)
                throw new PriceIntegrityException($"Corporate-action factor conflict on {group.Key:yyyy-MM-dd}.");
        }
        if (official.Any(x => !yahooDates.Contains(x.Date)))
            throw new PriceIntegrityException("Official/Yahoo corporate-action inventory differs.");
    }
}
