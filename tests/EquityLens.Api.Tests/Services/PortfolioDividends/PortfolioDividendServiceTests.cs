using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.PortfolioDividends;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.PortfolioDividends;

public sealed class PortfolioDividendServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid SecurityId1 = Guid.NewGuid();
    private static readonly Guid SecurityId2 = Guid.NewGuid();

    [Fact]
    public async Task GetLatestDividends_NoHoldings_ReturnsEmptyList()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db, PortfolioId, UserId, []);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetLatestDividendsAsync(PortfolioId, UserId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetLatestDividends_NoEvents_ReturnsEmptyList()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db, PortfolioId, UserId, [SecurityId1]);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetLatestDividendsAsync(PortfolioId, UserId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetLatestDividends_MultipleEventsPerSecurity_ReturnsLatestOnly()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db, PortfolioId, UserId, [SecurityId1]);
        db.CashDividendEvents.AddRange(
            CreateEvent(SecurityId1, new DateOnly(2024, 7, 1), 2m),
            CreateEvent(SecurityId1, new DateOnly(2025, 7, 1), 3m),
            CreateEvent(SecurityId1, new DateOnly(2023, 7, 1), 1m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetLatestDividendsAsync(PortfolioId, UserId);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(3m, result.Value![0].CashAmountPerShare);
        Assert.Equal(new DateOnly(2025, 7, 1), result.Value![0].ExDividendDate);
    }

    [Fact]
    public async Task GetLatestDividends_MultipleSecurities_ReturnsOnePerSecurity()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db, PortfolioId, UserId, [SecurityId1, SecurityId2]);
        db.CashDividendEvents.AddRange(
            CreateEvent(SecurityId1, new DateOnly(2025, 6, 15), 2m),
            CreateEvent(SecurityId2, new DateOnly(2025, 7, 1), 4m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetLatestDividendsAsync(PortfolioId, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, r => r.SecurityId == SecurityId1);
        Assert.Contains(result.Value, r => r.SecurityId == SecurityId2);
    }

    [Fact]
    public async Task GetLatestDividends_PortfolioOwnedByOtherUser_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db, PortfolioId, OtherUserId, [SecurityId1]);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GetLatestDividendsAsync(PortfolioId, UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("portfolio.not_found", result.ErrorCode);
    }

    private static PortfolioDividendService CreateService(EquityLensDbContext db)
    {
        return new PortfolioDividendService(db, new PortfolioRepository(db));
    }

    private static void SeedPortfolio(EquityLensDbContext db, Guid portfolioId, Guid ownerUserId, List<Guid> securityIds)
    {
        db.Portfolios.Add(new Portfolio
        {
            Id = portfolioId,
            OwnerUserId = ownerUserId,
            Name = "Test Portfolio",
            BaseCurrency = "TWD",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        foreach (var securityId in securityIds)
        {
            db.Securities.Add(new Security
            {
                Id = securityId,
                Ticker = $"T{securityId.ToString()[..4]}",
                Exchange = "TWSE",
                Name = $"Security {securityId}",
                Currency = "TWD",
                IsActive = true,
            });
            db.PortfolioHoldings.Add(new PortfolioHolding
            {
                Id = Guid.NewGuid(),
                PortfolioId = portfolioId,
                SecurityId = securityId,
                Quantity = 100m,
                AverageCost = 100m,
                CostCurrency = "TWD",
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
    }

    private static CashDividendEvent CreateEvent(Guid securityId, DateOnly exDate, decimal amount)
    {
        return new CashDividendEvent
        {
            Id = Guid.NewGuid(),
            SecurityId = securityId,
            ExDividendDate = exDate,
            PaymentDate = exDate.AddDays(20),
            CashAmountPerShare = amount,
            Currency = "TWD",
            Source = "FinMind",
            SourceKey = $"{securityId}:{exDate:yyyy-MM-dd}:{amount}",
        };
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
        public TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }
}
