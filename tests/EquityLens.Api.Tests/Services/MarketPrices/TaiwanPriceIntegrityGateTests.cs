using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.MarketPrices;

// 這組 InMemory 測試只驗證提交前拒絕；交易驗證在 PostgreSQL 整合測試。
public sealed class TaiwanPriceIntegrityGateTests
{
    [Theory]
    [InlineData("missing_adjustment")]
    [InlineData("zero")]
    [InlineData("duplicate")]
    [InlineData("missing_day")]
    [InlineData("stale")]
    [InlineData("conflicting_events")]
    [InlineData("no_data")]
    public async Task InvalidSource_PreservesExistingPricesAndSyncMetadata(string failure)
    {
        var factory = new TestFactory();
        var id = Guid.NewGuid();
        var start = new DateOnly(2021, 4, 6);
        var end = start.AddDays(1);
        await using (var db = factory.CreateDbContext())
        {
            db.Securities.Add(new Security { Id = id, Ticker = "2330", Exchange = "TWSE", Name = "Test",
                PricesSyncedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), PricesSource = "old" });
            db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = id,
                PriceTime = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Open = 100, High = 100, Low = 100, Close = 100, AdjustedClose = 99, DataSource = "old" });
            await db.SaveChangesAsync();
        }
        var raw = new List<ImportedMarketPrice> { new(start, 100, 100, 100, failure == "zero" ? 0 : 100, null, 100),
            new(end, 100, 100, 100, 100, null, 100) };
        if (failure is "missing_day" or "stale") raw.RemoveAt(1);
        if (failure == "duplicate") raw.Add(raw[0]);
        if (failure == "no_data") raw.Clear();
        var adjusted = failure is "missing_adjustment" or "conflicting_events"
            ? new Dictionary<DateOnly, decimal>() : new Dictionary<DateOnly, decimal> { [start] = 98, [end] = 100 };
        var yahooActions = failure == "conflicting_events" ? new[] { new CorporatePriceAction(end, "cash", 10) } : [];
        var events = failure == "conflicting_events" ? new[] { new CorporatePriceAction(end, "combined", 0, 0.95m) } : [];
        var provider = new TestProvider(raw, new(adjusted, yahooActions, "{}", DateTime.UtcNow, true));
        var evidence = new TestEvidence(new(new HashSet<DateOnly> { start, end }, events,
            new HashSet<DateOnly>(), new Dictionary<DateOnly, ImportedMarketPrice>(), "{}", failure == "conflicting_events"));
        var service = new TaiwanPriceImportCoordinator(factory, [provider], evidence);
        var result = await service.ImportAsync(id, start, end, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("market_price.quality_error", result.ErrorCode);
        await using var verify = factory.CreateDbContext();
        var price = await verify.MarketPrices.SingleAsync();
        Assert.Equal(99m, price.AdjustedClose);
        Assert.Equal("old", price.DataSource);
        var security = await verify.Securities.SingleAsync();
        Assert.Equal("old", security.PricesSource);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), security.PricesSyncedAtUtc);
        Assert.Null(security.PriceAdjustmentBatchId);
        Assert.Empty(await verify.PriceAdjustmentBatches.ToListAsync());
    }

    internal sealed class TestFactory : IDbContextFactory<EquityLensDbContext>
    {
        private readonly DbContextOptions<EquityLensDbContext> _options =
            new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public EquityLensDbContext CreateDbContext() => new TestDb(_options);
        public Task<EquityLensDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Ignore<DocumentEmbedding>(); }
    }
    internal sealed class TestProvider(IReadOnlyList<ImportedMarketPrice> raw, AdjustmentSnapshot snapshot)
        : IMarketDataProvider, IAdjustedCloseProvider
    {
        public string SourceName => "FinMind";
        public bool Supports(string exchange) => true;
        public Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(Security security, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult(raw);
        public Task<AdjustmentSnapshot> GetAdjustmentSnapshotAsync(Security security, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(string query, CancellationToken ct) => throw new NotImplementedException();
        public Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(string ticker, string exchange, CancellationToken ct) => throw new NotImplementedException();
    }
    internal sealed class TestEvidence(TaiwanPriceEvidence evidence) : ITaiwanPriceEvidenceProvider
    {
        public Task<TaiwanPriceEvidence> GetEvidenceAsync(Security security, DateOnly from, DateOnly to,
            IReadOnlyList<ImportedMarketPrice> prices, bool requireActions, CancellationToken ct) => Task.FromResult(evidence);
    }
}
