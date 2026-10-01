using System.Text.Json;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Repositories.MarketPrices;
using Microsoft.Extensions.Logging.Abstractions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Tests.Services.MarketPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Integration;

[Collection(PortfolioTransactionPostgresCollection.Name)]
public sealed class TaiwanPriceImportPostgresTests(PostgresTransactionDatabaseFixture fixture)
{
    private static readonly DateOnly Start = new(2021, 4, 6);
    private static readonly DateOnly End = new(2025, 8, 1);

    [Fact]
    public async Task FullSnapshot_ReplacesAllOldAdjustments_AndRepeatDoesNotDuplicatePrices()
    {
        var seed = await SeedAsync();
        var coordinator = Coordinator();
        var first = await coordinator.ImportAsync(seed.Id, Start, End, default);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        await using (var verify = Context())
        {
            var rows = await verify.MarketPrices.Where(x => x.SecurityId == seed.Id).OrderBy(x => x.PriceTime).ToListAsync();
            Assert.Equal(new decimal?[] { 98m, 100m }, rows.Select(x => x.AdjustedClose).ToArray());
            Assert.All(rows, x => Assert.Equal(first.Value!.AdjustmentVersion, x.PriceAdjustmentBatchId));
            var security = await verify.Securities.SingleAsync(x => x.Id == seed.Id);
            Assert.Equal(first.Value!.AdjustmentVersion, security.PriceAdjustmentBatchId);
            Assert.Equal(End, security.PricesVerifiedThrough);
            var audit = await verify.PriceAdjustmentBatches.SingleAsync(x => x.Id == security.PriceAdjustmentBatchId);
            Assert.Equal(audit.SnapshotHash, TaiwanPriceImportCoordinator.Hash(audit.SnapshotJson));
        }
        var second = await coordinator.ImportAsync(seed.Id, End, End, default);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        await using var db = Context();
        Assert.Equal(2, await db.MarketPrices.CountAsync(x => x.SecurityId == seed.Id));
        Assert.Equal(2, await db.PriceAdjustmentBatches.CountAsync(x => x.SecurityId == seed.Id));
    }

    [Fact]
    public async Task MissingYahooValue_OfficialFactorRebuildsEveryHistoricalDate()
    {
        var seed = await SeedAsync();
        var provider = new TaiwanPriceIntegrityGateTests.TestProvider(Raw(), new(
            new Dictionary<DateOnly, decimal> { [End] = 100 }, [new(End, "cash", 2)], "{}", DateTime.UtcNow, true));
        var evidence = Evidence([new(End, "combined", 0, 0.98m)], true);
        var service = new TaiwanPriceImportCoordinator(new Factory(Options()), [provider], evidence);
        var result = await service.ImportAsync(seed.Id, Start, End, default);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        await using var verify = Context();
        var rows = await verify.MarketPrices.Where(x => x.SecurityId == seed.Id).OrderBy(x => x.PriceTime).ToListAsync();
        Assert.Equal(98m, rows[0].AdjustedClose);
        Assert.Equal(100m, rows[1].AdjustedClose);
        Assert.Equal("TWSE+FinMindOfficial", result.Value!.AdjustmentSource);
    }

    [Fact]
    public async Task YahooAndOfficialUnavailable_LeavesAllRowsAndMetadataUnchanged()
    {
        var seed = await SeedAsync();
        var before = await FingerprintAsync(seed.Id);
        var provider = new TaiwanPriceIntegrityGateTests.TestProvider(Raw(), new(
            new Dictionary<DateOnly, decimal>(), [], "{}", DateTime.UtcNow, false));
        var service = new TaiwanPriceImportCoordinator(new Factory(Options()), [provider], Evidence([], false));
        var result = await service.ImportAsync(seed.Id, Start, End, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("market_price.quality_error", result.ErrorCode);
        Assert.Equal(before, await FingerprintAsync(seed.Id));
        await using var verify = Context();
        Assert.Empty(await verify.PriceAdjustmentBatches.Where(x => x.SecurityId == seed.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterSave_RollsBackPricesBatchAndSyncState(bool cancel)
    {
        var seed = await SeedAsync();
        var before = await FingerprintAsync(seed.Id);
        var service = Coordinator(new FailureAfterSave(cancel));
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ImportAsync(seed.Id, Start, End, default));
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(seed.Id, Start, End, default));
        }
        Assert.Equal(before, await FingerprintAsync(seed.Id));
        await using var verify = Context();
        Assert.Empty(await verify.PriceAdjustmentBatches.Where(x => x.SecurityId == seed.Id).ToListAsync());
        // 下一個獨立工作單元沒有失敗追蹤狀態，可正常重試。
        Assert.True((await Coordinator().ImportAsync(seed.Id, Start, End, default)).IsSuccess);
    }

    [Fact]
    public async Task TwoCandidatesFromSameVersion_OnlyOneCommits()
    {
        var seed = await SeedAsync();
        var before = await FingerprintAsync(seed.Id);
        var coordinator = Coordinator();
        var series = Raw().Select(x => x with { AdjustedClose = x.Date == Start ? 98 : 100 }).ToList();
        var results = await Task.WhenAll(
            coordinator.CommitAsync(seed.Id, before, series, new HashSet<DateOnly>(), Batch(seed.Id), Start, End, default),
            coordinator.CommitAsync(seed.Id, before, series, new HashSet<DateOnly>(), Batch(seed.Id), Start, End, default));
        Assert.Single(results, x => x.IsSuccess);
        Assert.Single(results, x => x.ErrorCode == "market_price.update_conflict");
        await using var verify = Context();
        Assert.Equal(1, await verify.PriceAdjustmentBatches.CountAsync(x => x.SecurityId == seed.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LeadingCalendarGap_RequiresExplicitNoTradeEvidence(bool hasExistingPrices, bool confirmedNoTrade)
    {
        var seed = await SeedAsync(hasExistingPrices ? Raw() : []);
        var before = await FingerprintAsync(seed.Id);
        var requestedFrom = Start.AddDays(-5);
        var provider = new TaiwanPriceIntegrityGateTests.TestProvider(Raw(), new(
            new Dictionary<DateOnly, decimal> { [Start] = 98, [End] = 100 }, [], "{}", DateTime.UtcNow, true));
        var noTrades = confirmedNoTrade ? new HashSet<DateOnly> { requestedFrom } : [];
        var evidence = new TaiwanPriceIntegrityGateTests.TestEvidence(new(
            new HashSet<DateOnly> { requestedFrom, Start, End }, [], noTrades,
            new Dictionary<DateOnly, ImportedMarketPrice>(), "{}", false));
        var coordinator = new TaiwanPriceImportCoordinator(new Factory(Options()), [provider], evidence);
        var result = await coordinator.ImportAsync(seed.Id, requestedFrom, End, default);
        await using var db = Context();
        if (confirmedNoTrade)
        {
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(requestedFrom, result.Value!.CoverageFrom);
            Assert.Equal(2, await db.MarketPrices.CountAsync(x => x.SecurityId == seed.Id));
            Assert.DoesNotContain(await db.MarketPrices.Where(x => x.SecurityId == seed.Id).ToListAsync(),
                x => DateOnly.FromDateTime(x.PriceTime) == requestedFrom);
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Equal("market_price.quality_error", result.ErrorCode);
            Assert.Equal(before, await FingerprintAsync(seed.Id));
            Assert.Empty(await db.PriceAdjustmentBatches.Where(x => x.SecurityId == seed.Id).ToListAsync());
        }
    }

    [Theory]
    [InlineData("first")]
    [InlineData("middle")]
    [InlineData("last")]
    public async Task SyncAfterPriceDeletion_RefetchesEntireBatchInsteadOfSkipping(string missing)
    {
        var to = TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.UtcNow);
        var from = to.AddDays(-10);
        var dates = Enumerable.Range(0, 11).Select(i => from.AddDays(i))
            .Where(x => x.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
        var raw = dates.Select(x => new ImportedMarketPrice(x, 100, 100, 100, 100, null, 100)).ToArray();
        var seed = await SeedAsync([]);
        var provider = new TaiwanPriceIntegrityGateTests.TestProvider(raw, new(
            dates.ToDictionary(x => x, _ => 100m), [], "{}", DateTime.UtcNow, true));
        var sources = JsonSerializer.Serialize(new[] { new { source = "FinMind:TaiwanStockTradingDate",
            snapshot = JsonSerializer.Serialize(new { data = dates.Select(x => new { date = x.ToString("yyyy-MM-dd") }) }) } });
        var evidence = new TaiwanPriceIntegrityGateTests.TestEvidence(new(dates.ToHashSet(), [],
            new HashSet<DateOnly>(), new Dictionary<DateOnly, ImportedMarketPrice>(), sources, false));
        var coordinator = new TaiwanPriceImportCoordinator(new Factory(Options()), [provider], evidence);
        var original = await coordinator.ImportAsync(seed.Id, from, to, default);
        Assert.True(original.IsSuccess, original.ErrorMessage);
        var deletedDate = missing switch { "first" => dates[0], "middle" => dates[^2], _ => dates[^1] };
        await using (var db = Context())
        {
            var row = await db.MarketPrices.SingleAsync(x => x.SecurityId == seed.Id &&
                x.PriceTime == deletedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            db.Remove(row);
            await db.SaveChangesAsync();
        }
        await using var read = Context();
        var service = Service(read, provider, coordinator);
        var result = await service.SyncDailyPricesAsync(seed.Id, 2, false, default);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.Value!.Skipped);
        Assert.True(result.Value.Synced);
        Assert.NotEqual(original.Value!.AdjustmentVersion, result.Value.AdjustmentVersion);
        Assert.Equal(dates, (await read.MarketPrices.Where(x => x.SecurityId == seed.Id).OrderBy(x => x.PriceTime)
            .Select(x => x.PriceTime).ToListAsync()).Select(DateOnly.FromDateTime).ToArray());
    }

    [Fact]
    public async Task ImportByTicker_ReturnsCommittedAdjustmentMetadata()
    {
        var seed = await SeedAsync();
        var provider = new TaiwanPriceIntegrityGateTests.TestProvider(Raw(), new(
            new Dictionary<DateOnly, decimal> { [Start] = 98, [End] = 100 }, [], "{}", DateTime.UtcNow, true));
        var coordinator = new TaiwanPriceImportCoordinator(new Factory(Options()), [provider], Evidence([], false));
        await using var read = Context();
        var result = await Service(read, provider, coordinator).ImportDailyPricesByTickerAsync(
            new(seed.Ticker, seed.Exchange, Start, End, null, null, null, null, null, null), default);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        await using var verify = Context();
        var batch = await verify.PriceAdjustmentBatches.SingleAsync(x => x.SecurityId == seed.Id);
        var value = result.Value!;
        Assert.Equal(batch.Source, value.AdjustmentSource);
        Assert.Equal(batch.Id, value.AdjustmentVersion);
        Assert.Equal(batch.From, value.CoverageFrom);
        Assert.Equal(batch.To, value.CoverageTo);
        Assert.Equal(batch.VerifiedThrough, value.VerifiedThrough);
    }

    private static MarketPriceService Service(EquityLensDbContext db, IMarketDataProvider provider,
        TaiwanPriceImportCoordinator coordinator) => new(db, new SecurityRepository(db), new MarketPriceRepository(db),
            [provider], NullLogger<MarketPriceService>.Instance, coordinator);

    private TaiwanPriceImportCoordinator Coordinator(SaveChangesInterceptor? interceptor = null) =>
        new(new Factory(Options(interceptor)),
            [new TaiwanPriceIntegrityGateTests.TestProvider(Raw(), new(
                new Dictionary<DateOnly, decimal> { [Start] = 98, [End] = 100 }, [], "{}", DateTime.UtcNow, true))],
            Evidence([], false));
    private static TaiwanPriceIntegrityGateTests.TestEvidence Evidence(IReadOnlyList<CorporatePriceAction> actions, bool complete) =>
        new(new(new HashSet<DateOnly> { Start, End }, actions, new HashSet<DateOnly>(),
            new Dictionary<DateOnly, ImportedMarketPrice>(), "{}", complete));
    private static IReadOnlyList<ImportedMarketPrice> Raw() =>
        [new(Start, 100, 100, 100, 100, null, 100), new(End, 100, 100, 100, 100, null, 100)];
    private static PriceAdjustmentBatch Batch(Guid id) => new()
    {
        Id = Guid.NewGuid(), SecurityId = id, FetchedAtUtc = DateTime.UtcNow, From = Start, To = End,
        VerifiedThrough = End, Source = "fixture", AlgorithmVersion = PriceAdjustmentChain.Version,
        SnapshotJson = "{}", SnapshotHash = TaiwanPriceImportCoordinator.Hash("{}"), SeriesHash = "fixture"
    };
    private async Task<Security> SeedAsync(IReadOnlyList<ImportedMarketPrice>? prices = null)
    {
        await using var db = Context();
        var s = new Security { Id = Guid.NewGuid(), Ticker = ("T" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant(),
            Exchange = "TWSE", Name = "fixture", PricesSource = "old",
            PricesSyncedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        db.Securities.Add(s);
        foreach (var p in prices ?? Raw())
            db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = s.Id,
                PriceTime = p.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Open = 100, High = 100,
                Low = 100, Close = 100, AdjustedClose = 99, DataSource = "old" });
        await db.SaveChangesAsync();
        return s;
    }
    private async Task<string> FingerprintAsync(Guid id)
    {
        await using var db = Context();
        return TaiwanPriceImportCoordinator.Fingerprint(
            await db.Securities.AsNoTracking().SingleAsync(x => x.Id == id),
            await db.MarketPrices.AsNoTracking().Where(x => x.SecurityId == id && x.Interval == "1d").ToListAsync());
    }
    private EquityLensDbContext Context() => new(Options());
    private DbContextOptions<EquityLensDbContext> Options(SaveChangesInterceptor? interceptor = null)
    {
        if (string.IsNullOrWhiteSpace(fixture.ConnectionString))
            throw Xunit.Sdk.SkipException.ForSkip("Set EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING for isolated PostgreSQL tests.");
        var builder = new DbContextOptionsBuilder<EquityLensDbContext>().UseNpgsql(fixture.ConnectionString, x => x.UseVector());
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return builder.Options;
    }
    private sealed class Factory(DbContextOptions<EquityLensDbContext> options) : IDbContextFactory<EquityLensDbContext>
    {
        public EquityLensDbContext CreateDbContext() => new(options);
        public Task<EquityLensDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class FailureAfterSave(bool cancel) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) =>
            cancel ? throw new OperationCanceledException("Injected cancellation after SaveChanges.")
                : throw new InvalidOperationException("Injected failure after SaveChanges.");
    }
}
