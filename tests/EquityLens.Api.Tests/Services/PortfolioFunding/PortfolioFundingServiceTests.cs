using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.PortfolioFunding;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.PortfolioFunding;

public sealed class PortfolioFundingServiceTests
{
    private static readonly Guid PortfolioId = Guid.NewGuid();

    [Fact]
    public async Task RebuildImplicitFunding_BuyWithoutCash_CreatesSystemDerivedDeposit()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db);
        var securityId = SeedSecurity(db);
        db.PortfolioTransactions.Add(Buy(securityId, 100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        var service = new PortfolioFundingService(db);
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        var flows = db.PortfolioCashFlows.ToList();
        Assert.Single(flows);
        Assert.Equal("Deposit", flows[0].FlowType);
        Assert.Equal(1000m, flows[0].Amount);
        Assert.True(flows[0].IsSystemDerived);
        Assert.Equal("系統推導：買入資金", flows[0].Note);
    }

    [Fact]
    public async Task RebuildImplicitFunding_BuyWithEnoughCash_DoesNotCreateDeposit()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db);
        var securityId = SeedSecurity(db);
        db.PortfolioCashFlows.Add(ManualDeposit(1000m, new DateOnly(2025, 1, 1)));
        db.PortfolioTransactions.Add(Buy(securityId, 100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        var service = new PortfolioFundingService(db);
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        var flows = db.PortfolioCashFlows.ToList();
        Assert.Single(flows);
        Assert.False(flows[0].IsSystemDerived);
    }

    [Fact]
    public async Task RebuildImplicitFunding_AfterDeletingBuyTransaction_RemovesImplicitDeposit()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db);
        var securityId = SeedSecurity(db);
        var buy = Buy(securityId, 100m, 10m, 0m, new DateOnly(2025, 1, 1));
        db.PortfolioTransactions.Add(buy);
        await db.SaveChangesAsync();

        var service = new PortfolioFundingService(db);
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Single(db.PortfolioCashFlows);

        db.PortfolioTransactions.Remove(buy);
        await db.SaveChangesAsync();
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(db.PortfolioCashFlows);
    }

    [Fact]
    public async Task RebuildImplicitFunding_SameDaySellProceedsReduceRequiredDeposit()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db);
        var securityId = SeedSecurity(db);
        var date = new DateOnly(2025, 1, 1);
        db.PortfolioTransactions.Add(Buy(securityId, 100m, 10m, 0m, date));
        db.PortfolioTransactions.Add(Sell(securityId, 50m, 12m, 0m, date));
        await db.SaveChangesAsync();

        var service = new PortfolioFundingService(db);
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        var flow = Assert.Single(db.PortfolioCashFlows);
        Assert.Equal(400m, flow.Amount); // 1000 cost - 600 proceeds
    }

    [Fact]
    public async Task RebuildImplicitFunding_NoteMatchesControllerSystemDerivedMarker()
    {
        await using var db = CreateDbContext();
        SeedPortfolio(db);
        var securityId = SeedSecurity(db);
        db.PortfolioTransactions.Add(Buy(securityId, 100m, 10m, 0m, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        var service = new PortfolioFundingService(db);
        await service.RebuildImplicitFundingAsync(PortfolioId, CancellationToken.None);
        await db.SaveChangesAsync();

        var flow = Assert.Single(db.PortfolioCashFlows);
        Assert.Equal("系統推導：買入資金", flow.Note);
    }

    private static void SeedPortfolio(EquityLensDbContext db)
    {
        db.Portfolios.Add(new Portfolio
        {
            Id = PortfolioId,
            OwnerUserId = Guid.NewGuid(),
            Name = "Test Portfolio",
            BaseCurrency = "TWD",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }

    private static Guid SeedSecurity(EquityLensDbContext db)
    {
        var id = Guid.NewGuid();
        db.Securities.Add(new Security
        {
            Id = id,
            Ticker = "2330",
            Exchange = "TWSE",
            Name = "Test Security",
            Currency = "TWD",
            IsActive = true,
        });
        return id;
    }

    private static PortfolioTransaction Buy(Guid securityId, decimal quantity, decimal price, decimal fee, DateOnly date)
        => CreateTransaction(securityId, "BUY", quantity, price, fee, date);

    private static PortfolioTransaction Sell(Guid securityId, decimal quantity, decimal price, decimal fee, DateOnly date)
        => CreateTransaction(securityId, "SELL", quantity, price, fee, date);

    private static PortfolioTransaction CreateTransaction(Guid securityId, string type, decimal quantity, decimal price, decimal fee, DateOnly date)
    {
        return new PortfolioTransaction
        {
            Id = Guid.NewGuid(),
            PortfolioId = PortfolioId,
            SecurityId = securityId,
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
}
