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
    public async Task ImportByTicker_TwseSecurity_BackfillsAdjustedCloseFromYahoo()
    {
        var db = CreateDb();
        db.Securities.Add(BuildSecurity("TWSE"));
        await db.SaveChangesAsync();

        var finmind = new FakeProvider("FinMind", [BuildPrice(From, null), BuildPrice(To, null)]);
        var yahoo = new FakeProvider("YahooFinance", [BuildPrice(From, 2320.5m), BuildPrice(To, 2410.25m)]);
        var service = CreateService(db, finmind, yahoo);

        var result = await service.ImportDailyPricesByTickerAsync(
            new ImportMarketPricesByTickerRequest("2330", "TWSE", From, To, null, null, null, null, null, null), default);

        Assert.True(result.IsSuccess);
        var rows = await db.MarketPrices.OrderBy(x => x.PriceTime).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.Equal("FinMind", x.DataSource));
        Assert.Equal(2320.5m, rows[0].AdjustedClose);
        Assert.Equal(2410.25m, rows[1].AdjustedClose);
    }

    [Fact]
    public async Task ImportByTicker_YahooBackfillFails_ImportStillSucceeds()
    {
        var db = CreateDb();
        db.Securities.Add(BuildSecurity("TWSE"));
        await db.SaveChangesAsync();

        var finmind = new FakeProvider("FinMind", [BuildPrice(From, null)]);
        var yahoo = new FakeProvider("YahooFinance", null);
        var service = CreateService(db, finmind, yahoo);

        var result = await service.ImportDailyPricesByTickerAsync(
            new ImportMarketPricesByTickerRequest("2330", "TWSE", From, To, null, null, null, null, null, null), default);

        Assert.True(result.IsSuccess);
        var row = await db.MarketPrices.SingleAsync();
        Assert.Null(row.AdjustedClose);
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
        public Task<Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
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
