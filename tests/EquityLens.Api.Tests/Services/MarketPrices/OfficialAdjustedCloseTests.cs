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

public sealed class OfficialAdjustedCloseTests
{
    private static readonly DateOnly D1 = new(2025, 6, 9), D2 = new(2025, 6, 10), D3 = new(2025, 6, 18), D4 = new(2025, 6, 19);

    [Fact]
    public void CashDividend_AdjustsOnlyEarlierDates_AndLatestEqualsClose()
    {
        var result = OfficialAdjustedCloseCalculator.Calculate(
            [(D1, 100m), (D2, 100m), (D3, 98m), (D4, 99m)],
            [new(D3, "dividend:息", 98m, 100m)]);

        Assert.True(result.Applied);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(99m, result.AdjustedCloseByDate[D4]);
        Assert.Equal(98m, result.AdjustedCloseByDate[D3]);
        Assert.Equal(98m, result.AdjustedCloseByDate[D2]); // 100 × 98/100：除息當天以參考價開盤，還原報酬為 0。
        Assert.Equal(98m, result.AdjustedCloseByDate[D1]);
    }

    [Fact]
    public void Split_0050_IsContinuousInsteadOfAMinus75PercentCrash()
    {
        // 0050 2025-06-18 一拆四：前一日 188.65，參考價 47.16，當日收 47.57。
        var result = OfficialAdjustedCloseCalculator.Calculate(
            [(D1, 183.7m), (D2, 188.65m), (D3, 47.57m)],
            [new(D3, "split:分割", 47.16m, 188.65m)]);

        Assert.True(result.Applied);
        var splitDayReturn = result.AdjustedCloseByDate[D3] / result.AdjustedCloseByDate[D2] - 1m;
        Assert.Equal(47.57m / 47.16m - 1m, splitDayReturn, 6);
    }

    [Fact]
    public void CapitalReduction_ReferenceAboveClose_IsNotAGain()
    {
        // 2317 2018-10-26 現金減資：前一日 68.1，參考價 82.62，當日收 76.2。
        DateOnly before = new(2018, 10, 17), after = new(2018, 10, 26);
        var result = OfficialAdjustedCloseCalculator.Calculate(
            [(before, 68.1m), (after, 76.2m)],
            [new(after, "capital_reduction", 82.62m, 68.1m)]);

        Assert.True(result.Applied);
        Assert.Equal(76.2m / 82.62m - 1m, result.AdjustedCloseByDate[after] / result.AdjustedCloseByDate[before] - 1m, 6);
    }

    [Fact]
    public void ExDateOnMarketHoliday_StillAppliesToEarlierDates()
    {
        // 2008-07-28 颱風休市，除權息日當天沒有價格。
        DateOnly a = new(2008, 7, 25), holiday = new(2008, 7, 28), b = new(2008, 7, 29);
        var result = OfficialAdjustedCloseCalculator.Calculate(
            [(a, 15.3m), (b, 13.38m)],
            [new(holiday, "dividend:權息", 13.92m, 15.3m)]);

        Assert.True(result.Applied);
        Assert.Equal(13.38m / 13.92m - 1m, result.AdjustedCloseByDate[b] / result.AdjustedCloseByDate[a] - 1m, 6);
    }

    [Fact]
    public void MissingCorporateAction_RejectsWholeSeries()
    {
        // 聯電 2007 減資換股：停止交易後 +22%，官方資料沒有對應事件。
        var result = OfficialAdjustedCloseCalculator.Calculate([(new(2007, 9, 19), 18m), (new(2007, 10, 9), 21.98m)], []);

        Assert.False(result.Applied);
        Assert.Empty(result.AdjustedCloseByDate);
        Assert.Contains(result.Issues, x => x.StartsWith("unexplained_move:2007-09-19->2007-10-09"));
    }

    [Fact]
    public void NonTradingZeroRows_CarryForwardWithoutBreakingReturns()
    {
        // FinMind 在停止交易日回傳全 0（例如 2317 2025-07-30）。
        DateOnly a = new(2025, 7, 29), halted = new(2025, 7, 30), b = new(2025, 7, 31);
        var result = OfficialAdjustedCloseCalculator.Calculate([(a, 171.5m), (halted, 0m), (b, 178m)], []);

        Assert.True(result.Applied);
        Assert.Equal(1, result.NonTradingDays);
        Assert.Equal(result.AdjustedCloseByDate[a], result.AdjustedCloseByDate[halted]);
        Assert.Equal(178m, result.AdjustedCloseByDate[b]);

        var leading = OfficialAdjustedCloseCalculator.Calculate([(a, 0m), (halted, 100m), (b, 101m)], []);
        Assert.True(leading.Applied);
        Assert.False(leading.AdjustedCloseByDate.ContainsKey(a));
    }

    [Theory]
    [InlineData(2014, false)] // 舊制 7% 漲跌幅：9% 不可能是正常交易。
    [InlineData(2016, true)]  // 2015-06 起 10%：9% 是正常交易日。
    public void PriceLimitDependsOnDate(int year, bool applied)
    {
        var result = OfficialAdjustedCloseCalculator.Calculate([(new(year, 3, 1), 100m), (new(year, 3, 2), 109m)], []);
        Assert.Equal(applied, result.Applied);
    }

    [Fact]
    public void BeforePriceMismatch_Rejects_AndOutOfRangeEventsAreIgnored()
    {
        var mismatch = OfficialAdjustedCloseCalculator.Calculate(
            [(D1, 100m), (D2, 100m), (D3, 98m)], [new(D3, "dividend:息", 98m, 120m)]);
        Assert.False(mismatch.Applied);
        Assert.Contains(mismatch.Issues, x => x.StartsWith("before_price_mismatch"));

        var outside = OfficialAdjustedCloseCalculator.Calculate(
            [(D2, 100m), (D3, 101m)], [new(D1, "dividend:息", 1m, 999m), new(D4, "dividend:息", 1m, 999m)]);
        Assert.True(outside.Applied);
        Assert.Equal(0, outside.EventCount);
        Assert.Equal(100m, outside.AdjustedCloseByDate[D2]);
    }

    [Fact]
    public async Task Rebuilder_UpdatesOnlyChangedRows_AndIsIdempotent()
    {
        await using var db = CreateDb();
        var security = await SeedAsync(db, (D1, 100m, 50m), (D2, 100m, 98m), (D3, 98m, 98m), (D4, 99m, 99m));
        var provider = new FakeActions([new(D3, "dividend:息", 98m, 100m)]);
        var rebuilder = new OfficialAdjustedCloseRebuilder(db, provider, NullLogger<OfficialAdjustedCloseRebuilder>.Instance);

        var first = await rebuilder.RebuildAsync(security, default);
        Assert.True(first.Applied);
        Assert.Equal(1, first.UpdatedCount); // 只有 D1 的舊值 50 是錯的。
        Assert.Equal((D1, D4), (provider.From, provider.To));
        Assert.Equal(new[] { 98m, 98m, 98m, 99m }, await db.MarketPrices.OrderBy(x => x.PriceTime).Select(x => x.AdjustedClose!.Value).ToListAsync());

        var second = await rebuilder.RebuildAsync(security, default);
        Assert.True(second.Applied);
        Assert.Equal(0, second.UpdatedCount);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("provider_down")]
    [InlineData("validation")]
    [InlineData("us_exchange")]
    public async Task Rebuilder_LeavesExistingValuesUntouchedWhenNotSafe(string mode)
    {
        await using var db = CreateDb();
        var security = await SeedAsync(db, (D1, 100m, 50m), (D2, 100m, 50m), (D3, 98m, 49m));
        if (mode == "mixed") (await db.MarketPrices.FirstAsync()).DataSource = "YahooFinance";
        if (mode == "us_exchange") security.Exchange = "NASDAQ";
        await db.SaveChangesAsync();
        // validation：官方事件的前一日收盤價與資料庫不符。
        var provider = new FakeActions(mode == "provider_down" ? null : [new(D3, "dividend:息", 98m, mode == "validation" ? 120m : 100m)]);
        var rebuilder = new OfficialAdjustedCloseRebuilder(db, provider, NullLogger<OfficialAdjustedCloseRebuilder>.Instance);

        var result = await rebuilder.RebuildAsync(security, default);

        Assert.False(result.Applied);
        Assert.Equal(mode switch { "mixed" => "mixed_price_sources", "provider_down" => "corporate_actions_unavailable",
            "validation" => "validation_failed", _ => "unsupported_exchange" }, result.Reason);
        Assert.Equal(new[] { 50m, 50m, 49m }, await db.MarketPrices.OrderBy(x => x.PriceTime).Select(x => x.AdjustedClose!.Value).ToListAsync());
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Sync_UsesYahooBackfillOnlyWhenOfficialRebuildIsNotApplied(bool applied, int expectedYahooCalls)
    {
        await using var db = CreateDb();
        db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "2330", Exchange = "TWSE", Name = "台積電", AssetType = "Stock", Currency = "TWD", IsActive = true });
        await db.SaveChangesAsync();
        var finmind = new FakePrices("FinMind", [new(D1, 100, 100, 100, 100, null, 1), new(D2, 101, 101, 101, 101, null, 1)]);
        var yahoo = new FakePrices("YahooFinance", [new(D1, 100, 100, 100, 100, 90, 1), new(D2, 101, 101, 101, 101, 91, 1)]);
        var rebuilder = new FakeRebuilder(applied);
        var service = new MarketPriceService(db, new Securities(db), new MarketPriceRepository(db), [finmind, yahoo],
            NullLogger<MarketPriceService>.Instance, rebuilder);

        var result = await service.ImportDailyPricesByTickerAsync(new ImportMarketPricesByTickerRequest("2330", "TWSE", D1, D2, null, null, null, null, null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, rebuilder.Calls);
        Assert.Equal(expectedYahooCalls, yahoo.Calls);
    }

    private static async Task<Security> SeedAsync(EquityLensDbContext db, params (DateOnly Date, decimal Close, decimal Adjusted)[] rows)
    {
        var security = new Security { Id = Guid.NewGuid(), Ticker = "2330", Exchange = "TWSE", Name = "台積電", AssetType = "Stock", Currency = "TWD", IsActive = true };
        db.Securities.Add(security);
        foreach (var (date, close, adjusted) in rows)
            db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = security.Id, Interval = "1d",
                PriceTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Open = close, High = close, Low = close, Close = close,
                AdjustedClose = adjusted, DataSource = "FinMind" });
        await db.SaveChangesAsync();
        return security;
    }

    private static EquityLensDbContext CreateDb() => new TestDbContext(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeActions(IReadOnlyList<CorporateAction>? actions) : ICorporateActionProvider
    {
        public DateOnly From { get; private set; }
        public DateOnly To { get; private set; }
        public Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            (From, To) = (from, to);
            return actions is null ? throw new HttpRequestException("simulated FinMind outage") : Task.FromResult(actions);
        }
    }

    private sealed class FakeRebuilder(bool applied) : IOfficialAdjustedCloseRebuilder
    {
        public int Calls { get; private set; }
        public Task<OfficialAdjustedCloseRebuildResult> RebuildAsync(Security security, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new OfficialAdjustedCloseRebuildResult(applied, applied ? "applied" : "validation_failed", 0, 0, []));
        }
    }

    private sealed class FakePrices(string sourceName, IReadOnlyList<ImportedMarketPrice> prices) : IMarketDataProvider
    {
        public string SourceName { get; } = sourceName;
        public int Calls { get; private set; }
        public bool Supports(string exchange) => true;
        public Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(prices);
        }
        public Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(string query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(string ticker, string exchange, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class Securities(EquityLensDbContext db) : ISecurityRepository
    {
        public Task<Security?> GetEntityByTickerExchangeAsync(string ticker, string exchange, CancellationToken cancellationToken) =>
            db.Securities.SingleOrDefaultAsync(x => x.Ticker == ticker && x.Exchange == exchange, cancellationToken);
        public Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SecurityResponse?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ActiveExistsAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Security>> GetActiveEntitiesAsync(int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Add(Security security) => throw new NotImplementedException();
    }
}
