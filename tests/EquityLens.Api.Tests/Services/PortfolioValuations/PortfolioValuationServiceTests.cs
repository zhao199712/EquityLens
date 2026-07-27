using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.PortfolioFunding;
using EquityLens.Api.Services.PortfolioValuations;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.PortfolioValuations;

public sealed class PortfolioValuationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid SecurityId = Guid.NewGuid();

    [Fact]
    public async Task GetValuationHistory_SameDayDepositAndBuy_DepositAppliedBeforeBuy()
    {
        await using var db = CreateDbContext();
        SeedPortfolioWithHolding(db);
        var manualDeposit = ManualDeposit(1000m, new DateOnly(2025, 1, 1));
        var buy = Buy(100m, 10m, 0m, new DateOnly(2025, 1, 1));
        db.PortfolioCashFlows.Add(manualDeposit);
        db.PortfolioTransactions.Add(buy);
        SeedPrices(db, new[] { (new DateOnly(2025, 1, 1), 10m), (new DateOnly(2025, 1, 2), 11m) });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var points = result.Value!.Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(1000m, points[0].TotalAssetValue); // deposit + buy same day, cash just enough
        Assert.Equal(1100m, points[1].TotalAssetValue); // price rises to 11
        Assert.True(points[0].CashBalance >= 0);
    }

    [Fact]
    public async Task GetValuationHistory_DividendPosted_IncreasesCashAndTotalAsset()
    {
        await using var db = CreateDbContext();
        SeedPortfolioWithHolding(db);
        db.PortfolioTransactions.Add(Buy(100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        db.PortfolioCashFlows.Add(SystemDeposit(1000m, new DateOnly(2025, 1, 1)));
        db.PortfolioCashFlows.Add(Dividend(100m, new DateOnly(2025, 1, 2)));
        SeedPrices(db, new[] { (new DateOnly(2025, 1, 1), 10m), (new DateOnly(2025, 1, 2), 10m) });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var points = result.Value!.Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(1000m, points[0].TotalAssetValue);
        Assert.Equal(1100m, points[1].TotalAssetValue); // 1000 market + 100 cash dividend
    }

    [Fact]
    public async Task GetValuationHistory_AfterDeletingSellTrade_RealizedPnlAndHoldingReset()
    {
        await using var db = CreateDbContext();
        SeedPortfolioWithHolding(db);
        var buy = Buy(100m, 10m, 0m, new DateOnly(2025, 1, 1));
        var sell = Sell(50m, 12m, 0m, new DateOnly(2025, 1, 2));
        db.PortfolioTransactions.AddRange(buy, sell);
        db.PortfolioCashFlows.Add(SystemDeposit(1000m, new DateOnly(2025, 1, 1)));
        SeedPrices(db, new[]
        {
            (new DateOnly(2025, 1, 1), 10m),
            (new DateOnly(2025, 1, 2), 12m),
            (new DateOnly(2025, 1, 3), 12m),
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var beforeDelete = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), CancellationToken.None);
        Assert.True(beforeDelete.IsSuccess);
        Assert.Equal(100m, beforeDelete.Value!.Points[^1].TotalRealizedPnl); // 50 * (12 - 10)

        db.PortfolioTransactions.Remove(sell);
        await db.SaveChangesAsync();
        await new PortfolioFundingService(db).RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        var afterDelete = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), CancellationToken.None);
        Assert.True(afterDelete.IsSuccess);
        Assert.Equal(0m, afterDelete.Value!.Points[^1].TotalRealizedPnl);
        Assert.Equal(1200m, afterDelete.Value!.Points[^1].TotalMarketValue); // 100 shares @ 12
    }

    [Fact]
    public async Task GetValuationHistory_HoldingReducedToZero_MarketValueAndHoldingCountZero()
    {
        await using var db = CreateDbContext();
        SeedPortfolioWithHolding(db);
        db.PortfolioTransactions.Add(Buy(100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        db.PortfolioTransactions.Add(Sell(100m, 12m, 0m, new DateOnly(2025, 1, 2)));
        db.PortfolioCashFlows.Add(SystemDeposit(1000m, new DateOnly(2025, 1, 1)));
        SeedPrices(db, new[] { (new DateOnly(2025, 1, 1), 10m), (new DateOnly(2025, 1, 2), 12m) });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var lastPoint = result.Value!.Points[^1];
        Assert.Equal(0m, lastPoint.TotalMarketValue);
        Assert.Equal(0, lastPoint.HoldingCount);
        Assert.Equal(200m, lastPoint.TotalRealizedPnl);
    }

    [Fact]
    public async Task GetValuationHistory_WithBenchmarkData_ReturnsBetaAndJensenAlpha()
    {
        await using var db = CreateDbContext();
        SeedPortfolioWithHolding(db);
        db.PortfolioTransactions.Add(Buy(100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        db.PortfolioCashFlows.Add(SystemDeposit(1000m, new DateOnly(2025, 1, 1)));
        SeedPrices(db, new[]
        {
            (new DateOnly(2025, 1, 1), 10m),
            (new DateOnly(2025, 1, 2), 10.1m),
            (new DateOnly(2025, 1, 3), 10.2m),
            (new DateOnly(2025, 1, 4), 10.3m),
        });
        await db.SaveChangesAsync();

        var benchmark = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2025, 1, 1)] = 10000m,
            [new DateOnly(2025, 1, 2)] = 10100m,
            [new DateOnly(2025, 1, 3)] = 10200m,
            [new DateOnly(2025, 1, 4)] = 10300m,
        };

        var service = CreateService(db, new FakePortfolioBenchmarkService(benchmark));
        var result = await service.GetValuationHistoryAsync(PortfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 4), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Beta);
        Assert.NotNull(result.Value!.JensenAlpha);
        Assert.True(result.Value!.Beta > 0);
    }

    private static void SeedPortfolioWithHolding(EquityLensDbContext db)
    {
        db.Portfolios.Add(new Portfolio
        {
            Id = PortfolioId,
            OwnerUserId = UserId,
            Name = "Test Portfolio",
            BaseCurrency = "TWD",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        db.Securities.Add(new Security
        {
            Id = SecurityId,
            Ticker = "2330",
            Exchange = "TWSE",
            Name = "Test Security",
            Currency = "TWD",
            IsActive = true,
        });
        db.PortfolioHoldings.Add(new PortfolioHolding
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            SecurityId = SecurityId,
            Quantity = 0m,
            AverageCost = 0m,
            CostCurrency = "TWD",
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }

    private static void SeedPrices(EquityLensDbContext db, IEnumerable<(DateOnly Date, decimal Close)> prices)
    {
        foreach (var (date, close) in prices)
        {
            db.MarketPrices.Add(new MarketPrice
            {
                Id = Guid.NewGuid(),
                SecurityId = SecurityId,
                PriceTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Interval = "1d",
                Open = close,
                High = close,
                Low = close,
                Close = close,
                AdjustedClose = close,
                Volume = 0,
                DataSource = "Test",
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
    }

    private static PortfolioTransaction Buy(decimal quantity, decimal price, decimal fee, DateOnly date)
        => CreateTransaction("BUY", quantity, price, fee, date);

    private static PortfolioTransaction Sell(decimal quantity, decimal price, decimal fee, DateOnly date)
        => CreateTransaction("SELL", quantity, price, fee, date);

    private static PortfolioTransaction CreateTransaction(string type, decimal quantity, decimal price, decimal fee, DateOnly date)
    {
        return new PortfolioTransaction
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            SecurityId = SecurityId,
            TransactionType = type,
            Quantity = quantity,
            Price = price,
            Fee = fee,
            TransactionDate = date,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    private static PortfolioCashFlow ManualDeposit(decimal amount, DateOnly date)
    {
        return new PortfolioCashFlow
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            FlowType = "Deposit",
            Amount = amount,
            Currency = "TWD",
            EffectiveDate = date,
            Status = "Posted",
            IsUserAdjusted = true,
            Note = "Manual deposit",
        };
    }

    private static PortfolioCashFlow SystemDeposit(decimal amount, DateOnly date)
    {
        return new PortfolioCashFlow
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            FlowType = "Deposit",
            Amount = amount,
            Currency = "TWD",
            EffectiveDate = date,
            Status = "Posted",
            IsSystemDerived = true,
            Note = "系統推導：買入資金",
        };
    }

    private static PortfolioCashFlow Dividend(decimal amount, DateOnly date)
    {
        return new PortfolioCashFlow
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            FlowType = "Dividend",
            Amount = amount,
            Currency = "TWD",
            EffectiveDate = date,
            Status = "Posted",
            Note = "Dividend",
        };
    }

    private static PortfolioValuationService CreateService(EquityLensDbContext db, IPortfolioBenchmarkService? benchmarkService = null)
    {
        return new PortfolioValuationService(
            new FakeCurrentUserContext(UserId),
            new PortfolioRepository(db),
            new FakeMarketPriceRepository(db),
            new TransactionRepository(db),
            new FakeExchangeRateService(),
            db,
            new PortfolioFundingService(db),
            benchmarkService ?? new FakePortfolioBenchmarkService());
    }

    private static EquityLensDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestEquityLensDbContext(options);
    }

    private sealed class TestEquityLensDbContext : EquityLensDbContext
    {
        public TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid UserId => userId;
        public string Email => "test@example.com";
        public string DisplayName => "Test";
        public bool IsAuthenticated => true;
    }

    private sealed class FakeExchangeRateService : IExchangeRateService
    {
        public Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken)
            => Task.FromResult(amount);

        public Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
            => Task.FromResult(1m);
    }

    private sealed class FakePortfolioBenchmarkService(Dictionary<DateOnly, decimal>? values = null) : IPortfolioBenchmarkService
    {
        public Task<IReadOnlyDictionary<DateOnly, decimal>> GetTotalReturnIndexAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<DateOnly, decimal>>(values ?? new Dictionary<DateOnly, decimal>());
    }

    private sealed class FakeMarketPriceRepository(EquityLensDbContext db) : IMarketPriceRepository
    {
        private readonly MarketPriceRepository _inner = new(db);

        public Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(IReadOnlyCollection<Guid> securityIds, string interval, CancellationToken cancellationToken)
            => _inner.GetLatestPricesAsync(securityIds, interval, cancellationToken);

        public Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(Guid securityId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
            => _inner.GetBySecurityAsync(securityId, from, to, cancellationToken);

        public Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(IReadOnlyList<MarketPrice> prices, CancellationToken cancellationToken)
            => _inner.UpsertDailyPricesAsync(prices, cancellationToken);

        public Task<int> UpdateAdjustedCloseAsync(Guid securityId, IReadOnlyDictionary<DateOnly, decimal> adjustedCloseByDate, CancellationToken cancellationToken)
            => _inner.UpdateAdjustedCloseAsync(securityId, adjustedCloseByDate, cancellationToken);
    }
}
