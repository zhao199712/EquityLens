using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.MarketPrices;

public sealed class MarketPriceServiceAdjustedCloseBackfillTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 7, 20);
    private static readonly DateOnly To = new(2026, 7, 21);

    [Fact]
    public async Task ImportByTicker_VerifiedImporterUnavailable_DoesNotWrite()
    {
        var db = CreateDb();
        db.Securities.Add(BuildSecurity("TWSE"));
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeProvider("FinMind", [BuildPrice(From, null)]), new FakeProvider("YahooFinance", null));
        var result = await service.ImportDailyPricesByTickerAsync(
            new ImportMarketPricesByTickerRequest("2330", "TWSE", From, To, null, null, null, null, null, null), default);
        Assert.False(result.IsSuccess);
        Assert.Equal("market_price.provider_error", result.ErrorCode);
        Assert.Empty(await db.MarketPrices.ToListAsync());
        Assert.Null((await db.Securities.SingleAsync()).PricesSyncedAtUtc);
    }

    [Fact]
    public async Task ImportByTicker_UsSecurity_SkipsBackfill()
    {
        var db = CreateDb();
        db.Securities.Add(BuildSecurity("NASDAQ"));
        await db.SaveChangesAsync();

        var finmind = new FakeProvider("FinMind", [BuildPrice(From, 100m)]);
        var yahoo = new FakeProvider("YahooFinance", [BuildPrice(From, 99m)]);
        var service = CreateService(db, finmind, yahoo);

        var result = await service.ImportDailyPricesByTickerAsync(
            new ImportMarketPricesByTickerRequest("AAPL", "NASDAQ", From, To, null, null, null, null, null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, finmind.GetDailyPricesCallCount);
        Assert.Equal(1, yahoo.GetDailyPricesCallCount);
    }

    [Theory]
    [InlineData("unverified")]
    [InlineData("valid")]
    [InlineData("null_adjustment")]
    [InlineData("insufficient_range")]
    [InlineData("wrong_row_version")]
    [InlineData("missing_first")]
    [InlineData("missing_day")]
    [InlineData("missing_cutoff")]
    [InlineData("valid_no_trade")]
    [InlineData("price_on_no_trade")]
    public async Task SyncToday_SkipsOnlyVerifiedCompleteBatch(string state)
    {
        var db = CreateDb();
        var s = BuildSecurity("TWSE");
        s.PricesSyncedAtUtc = DateTime.UtcNow;
        db.Securities.Add(s);
        var to = TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.UtcNow);
        var from = to.AddDays(-5);
        var dates = Enumerable.Range(0, 6).Select(i => from.AddDays(i)).Where(x => x.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        if (state != "unverified")
        {
            var id = Guid.NewGuid();
            var noTrades = state is "valid_no_trade" or "price_on_no_trade" ? new[] { dates[0] } : [];
            var snapshot = System.Text.Json.JsonSerializer.Serialize(new
            {
                noTrades,
                official = System.Text.Json.JsonSerializer.Serialize(new[] { new
                {
                    source = "FinMind:TaiwanStockTradingDate",
                    snapshot = System.Text.Json.JsonSerializer.Serialize(new { data = dates.Select(x => new { date = x.ToString("yyyy-MM-dd") }) })
                } })
            });
            db.PriceAdjustmentBatches.Add(new PriceAdjustmentBatch { Id = id, SecurityId = s.Id, From = state == "insufficient_range" ? to : from,
                To = to, VerifiedThrough = dates[^1], Source = "fixture", SnapshotJson = snapshot });
            s.PriceAdjustmentBatchId = id; s.PricesVerifiedThrough = dates[^1];
            var missing = state switch
            {
                "missing_first" or "valid_no_trade" => (DateOnly?)dates[0],
                "missing_day" => dates[1],
                "missing_cutoff" => dates[^1],
                _ => null
            };
            foreach (var day in dates.Where(x => x != missing))
                db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = s.Id,
                    PriceTime = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Open = 100, High = 100, Low = 100,
                    Close = 100, AdjustedClose = state == "null_adjustment" ? null : 100,
                    PriceAdjustmentBatchId = state == "wrong_row_version" ? null : id });
        }
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeProvider("FinMind", null), new FakeProvider("YahooFinance", null));
        var result = await service.SyncDailyPricesAsync(s.Id, 5, false, default);
        if (state is "valid" or "valid_no_trade")
        {
            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Skipped);
            Assert.Equal(s.PriceAdjustmentBatchId, result.Value.AdjustmentVersion);
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Equal("market_price.provider_error", result.ErrorCode);
        }
    }

    private static Security BuildSecurity(string exchange) => new()
    {
        Id = SecurityId,
        Ticker = exchange is "TWSE" or "TPEX" ? "2330" : "AAPL",
        Exchange = exchange,
        Name = "Test",
        AssetType = "Stock",
        Currency = exchange is "TWSE" or "TPEX" ? "TWD" : "USD",
        IsActive = true
    };

    private static ImportedMarketPrice BuildPrice(DateOnly date, decimal? adjustedClose) =>
        new(date, 100m, 110m, 90m, 105m, adjustedClose, 1000);

    private static MarketPriceService CreateService(
        EquityLensDbContext db,
        FakeProvider finmind,
        FakeProvider yahoo)
    {
        return new MarketPriceService(
            db,
            new FakeSecurityRepository(db),
            new MarketPriceRepository(db),
            [finmind, yahoo],
            NullLogger<MarketPriceService>.Instance);
    }

    private static EquityLensDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private sealed class TestDbContext : EquityLensDbContext
    {
        public TestDbContext(DbContextOptions<EquityLensDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeSecurityRepository : ISecurityRepository
    {
        private readonly EquityLensDbContext _db;
        public FakeSecurityRepository(EquityLensDbContext db) => _db = db;

        public Task<Security?> GetEntityByTickerExchangeAsync(string ticker, string exchange, CancellationToken cancellationToken) =>
            _db.Securities.SingleOrDefaultAsync(x => x.Ticker == ticker && x.Exchange == exchange, cancellationToken);

        public Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SecurityResponse?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
            _db.Securities.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        public Task<bool> ActiveExistsAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Security>> GetActiveEntitiesAsync(int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Add(Security security) => throw new NotImplementedException();
    }

    private sealed class FakeProvider : IMarketDataProvider
    {
        private readonly IReadOnlyList<ImportedMarketPrice>? _prices;

        public FakeProvider(string sourceName, IReadOnlyList<ImportedMarketPrice>? prices)
        {
            SourceName = sourceName;
            _prices = prices;
        }

        public string SourceName { get; }
        public int GetDailyPricesCallCount { get; private set; }

        public bool Supports(string exchange) => true;

        public Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
            Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            GetDailyPricesCallCount++;
            if (_prices is null)
            {
                throw new HttpRequestException("simulated provider failure");
            }

            return Task.FromResult(_prices);
        }

        public Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(string query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(string ticker, string exchange, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
