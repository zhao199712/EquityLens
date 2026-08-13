using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioTransactions;
using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.PortfolioFunding;
using EquityLens.Api.Services.PortfolioTransactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Integration;

[CollectionDefinition("PostgreSQL transaction integration", DisableParallelization = true)]
public sealed class PortfolioTransactionPostgresCollection : ICollectionFixture<PostgresTransactionDatabaseFixture>
{
    public const string Name = "PostgreSQL transaction integration";
}

[Collection(PortfolioTransactionPostgresCollection.Name)]
public sealed class PortfolioTransactionPostgresIntegrationTests
{
    private readonly PostgresTransactionDatabaseFixture _fixture;

    public PortfolioTransactionPostgresIntegrationTests(PostgresTransactionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CleanPostgresDatabase_AppliesAllMigrations()
    {
        RequirePostgres();
        await using var db = CreateDbContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task MissingIdempotencyKey_IsRejectedByApi()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync();
        await using var db = CreateDbContext();
        var controller = new PortfolioTransactionsController(CreateService(db, seeded.UserId));

        var action = await controller.CreateTransaction(
            seeded.PortfolioId,
            BuyRequest(seeded.SecurityId),
            null!,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        await using var verify = CreateDbContext();
        Assert.Empty(await verify.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
    }

    [Fact]
    public async Task SameKeyAndPayload_ReturnsOriginalTransactionWithoutCreatingSecondRow()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync();

        var first = await CreateTransactionAsync(seeded, "same-key", BuyRequest(seeded.SecurityId));
        var replay = await CreateTransactionAsync(seeded, " same-key ", BuyRequest(seeded.SecurityId));

        Assert.True(first.IsSuccess);
        Assert.False(first.Value!.WasIdempotentReplay);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value!.WasIdempotentReplay);
        Assert.Equal(first.Value.Transaction.Id, replay.Value.Transaction.Id);

        await using var db = CreateDbContext();
        var rows = await db.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("same-key", row.IdempotencyKey);
        Assert.Matches("^[0-9a-f]{64}$", row.RequestHash!);
    }

    [Fact]
    public async Task SameKeyAndDifferentPayload_ReturnsConflict()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync();

        var first = await CreateTransactionAsync(seeded, "payload-key", BuyRequest(seeded.SecurityId, quantity: 10m));
        var conflict = await CreateTransactionAsync(seeded, "payload-key", BuyRequest(seeded.SecurityId, quantity: 11m));

        Assert.True(first.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("transaction.idempotency_conflict", conflict.ErrorCode);

        await using (var conflictDb = CreateDbContext())
        {
            var controller = new PortfolioTransactionsController(CreateService(conflictDb, seeded.UserId));
            var action = await controller.CreateTransaction(
                seeded.PortfolioId,
                BuyRequest(seeded.SecurityId, quantity: 12m),
                "payload-key",
                CancellationToken.None);
            Assert.IsType<ConflictObjectResult>(action.Result);
        }

        await using var db = CreateDbContext();
        Assert.Single(await db.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentSameKey_CreatesExactlyOneTransaction()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync();
        var request = BuyRequest(seeded.SecurityId);

        var results = await Task.WhenAll(
            CreateTransactionAsync(seeded, "concurrent-key", request),
            CreateTransactionAsync(seeded, "concurrent-key", request));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results.Where(result => !result.Value!.WasIdempotentReplay));
        Assert.Single(results.Where(result => result.Value!.WasIdempotentReplay));
        Assert.Equal(results[0].Value!.Transaction.Id, results[1].Value!.Transaction.Id);

        await using var db = CreateDbContext();
        Assert.Single(await db.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
    }

    [Fact]
    public async Task Create_ReconcilesHoldingDividendAndImplicitFunding()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync(includeDividend: true);
        var result = await CreateTransactionAsync(
            seeded,
            "derived-key",
            BuyRequest(seeded.SecurityId, quantity: 100m, price: 10m, date: new DateOnly(2025, 1, 1)));

        Assert.True(result.IsSuccess);
        await using var db = CreateDbContext();
        var holding = Assert.Single(await db.PortfolioHoldings
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.SecurityId == seeded.SecurityId)
            .ToListAsync());
        Assert.Equal(100m, holding.Quantity);
        Assert.Equal(10m, holding.AverageCost);

        var dividend = Assert.Single(await db.PortfolioCashFlows
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.FlowType == "Dividend")
            .ToListAsync());
        Assert.Equal(200m, dividend.Amount);
        Assert.True(dividend.IsSystemDerived);
        Assert.Equal("Posted", dividend.Status);

        var funding = Assert.Single(await db.PortfolioCashFlows
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.IsSystemDerived && x.Note == "系統推導：買入資金")
            .ToListAsync());
        Assert.Equal(1000m, funding.Amount);
    }

    [Fact]
    public async Task DividendReconcile_UpdatesAutomaticFlowAndPreservesUserAdjustedFlow()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync(includeDividend: true);
        await CreateTransactionAsync(
            seeded,
            "dividend-first",
            BuyRequest(seeded.SecurityId, quantity: 100m, price: 10m, date: new DateOnly(2025, 1, 1)));

        await using (var db = CreateDbContext())
        {
            var flow = await db.PortfolioCashFlows.SingleAsync(x => x.PortfolioId == seeded.PortfolioId && x.FlowType == "Dividend");
            flow.IsUserAdjusted = true;
            flow.Amount = 999m;
            await db.SaveChangesAsync();
        }

        var result = await CreateTransactionAsync(
            seeded,
            "dividend-second",
            BuyRequest(seeded.SecurityId, quantity: 50m, price: 0m, date: new DateOnly(2025, 1, 2)));
        Assert.True(result.IsSuccess);

        await using var verify = CreateDbContext();
        var flowAfter = await verify.PortfolioCashFlows
            .SingleAsync(x => x.PortfolioId == seeded.PortfolioId && x.FlowType == "Dividend");
        Assert.Equal(999m, flowAfter.Amount);
        Assert.True(flowAfter.IsUserAdjusted);
        var holding = await verify.PortfolioHoldings
            .SingleAsync(x => x.PortfolioId == seeded.PortfolioId && x.SecurityId == seeded.SecurityId);
        Assert.Equal(150m, holding.Quantity);
        Assert.Equal(20m / 3m, holding.AverageCost, precision: 5);
    }

    [Fact]
    public async Task FundingFailure_RollsBackAllRowsAndAllowsSameKeyRetry()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync(includeDividend: true);
        var key = "retry-after-rollback";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateTransactionAsync(seeded, key, BuyRequest(seeded.SecurityId), new ThrowingFundingService()));

        await using (var failed = CreateDbContext())
        {
            Assert.Empty(await failed.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
            Assert.Empty(await failed.PortfolioHoldings.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
            Assert.Empty(await failed.PortfolioCashFlows.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
        }

        var retry = await CreateTransactionAsync(seeded, key, BuyRequest(seeded.SecurityId));
        Assert.True(retry.IsSuccess);
        Assert.False(retry.Value!.WasIdempotentReplay);
    }

    [Fact]
    public async Task DeleteFailure_RollsBackAndPreservesOriginalDerivedRows()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync(includeDividend: true);
        await CreateTransactionAsync(
            seeded,
            "delete-failure-seed",
            BuyRequest(seeded.SecurityId, quantity: 100m, price: 10m, date: new DateOnly(2025, 1, 1)));

        await using var deleteDb = CreateDbContext();
        var transaction = await deleteDb.PortfolioTransactions.SingleAsync(x => x.PortfolioId == seeded.PortfolioId);
        var service = CreateService(deleteDb, seeded.UserId, new ThrowingFundingService());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(seeded.PortfolioId, transaction.Id, CancellationToken.None));

        await using var verify = CreateDbContext();
        Assert.Single(await verify.PortfolioTransactions.Where(x => x.PortfolioId == seeded.PortfolioId).ToListAsync());
        Assert.Equal(100m, (await verify.PortfolioHoldings.SingleAsync(x => x.PortfolioId == seeded.PortfolioId)).Quantity);
        Assert.Equal(200m, (await verify.PortfolioCashFlows.SingleAsync(x => x.PortfolioId == seeded.PortfolioId && x.FlowType == "Dividend")).Amount);
        Assert.Equal(1000m, (await verify.PortfolioCashFlows.SingleAsync(x => x.PortfolioId == seeded.PortfolioId && x.Note == "系統推導：買入資金")).Amount);
    }

    [Fact]
    public async Task DeleteAllBySecurity_ReconcilesHoldingDividendAndFunding()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync(includeDividend: true, includeSecondSecurity: true);
        await CreateTransactionAsync(seeded, "delete-all-target", BuyRequest(seeded.SecurityId, quantity: 100m, price: 10m));
        await CreateTransactionAsync(seeded, "delete-all-other", BuyRequest(seeded.SecondSecurityId!.Value, quantity: 20m, price: 5m));

        await using (var deleteDb = CreateDbContext())
        {
            var service = CreateService(deleteDb, seeded.UserId);
            var result = await service.DeleteAllBySecurityAsync(seeded.PortfolioId, seeded.SecurityId, CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verify = CreateDbContext();
        Assert.Empty(await verify.PortfolioTransactions
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.SecurityId == seeded.SecurityId)
            .ToListAsync());
        Assert.Empty(await verify.PortfolioHoldings
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.SecurityId == seeded.SecurityId)
            .ToListAsync());
        Assert.Empty(await verify.PortfolioCashFlows
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.SecurityId == seeded.SecurityId && x.FlowType == "Dividend")
            .ToListAsync());
        var remainingFunding = Assert.Single(await verify.PortfolioCashFlows
            .Where(x => x.PortfolioId == seeded.PortfolioId && x.Note == "系統推導：買入資金")
            .ToListAsync());
        Assert.Equal(100m, remainingFunding.Amount);
    }

    [Fact]
    public async Task ApiValidationRejectsInvalidTypeQuantityPriceAndFee()
    {
        RequirePostgres();
        var seeded = await SeedPortfolioAsync();
        await using var db = CreateDbContext();
        var controller = new PortfolioTransactionsController(CreateService(db, seeded.UserId));

        var invalidRequests = new[]
        {
            BuyRequest(seeded.SecurityId) with { TransactionType = "HOLD" },
            BuyRequest(seeded.SecurityId) with { Quantity = 0m },
            BuyRequest(seeded.SecurityId) with { Price = -1m },
            BuyRequest(seeded.SecurityId) with { Fee = -1m },
        };

        foreach (var request in invalidRequests)
        {
            var action = await controller.CreateTransaction(
                seeded.PortfolioId,
                request,
                "validation-key-" + Guid.NewGuid().ToString("N"),
                CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(action.Result);
        }
    }

    [Fact]
    public async Task DatabaseConstraintsRejectInvalidTransactionValues()
    {
        RequirePostgres();
        var invalidRows = new[]
        {
            new InvalidRow("HOLD", 1m, 1m, 0m),
            new InvalidRow("BUY", 0m, 1m, 0m),
            new InvalidRow("BUY", 1m, -1m, 0m),
            new InvalidRow("BUY", 1m, 1m, -1m),
        };

        foreach (var invalid in invalidRows)
        {
            var seeded = await SeedPortfolioAsync();
            await using var db = CreateDbContext();
            db.PortfolioTransactions.Add(new PortfolioTransaction
            {
                Id = Guid.NewGuid(),
                PortfolioId = seeded.PortfolioId,
                SecurityId = seeded.SecurityId,
                TransactionType = invalid.Type,
                Quantity = invalid.Quantity,
                Price = invalid.Price,
                Fee = invalid.Fee,
                TransactionDate = new DateOnly(2025, 1, 1),
                CreatedAtUtc = DateTime.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private async Task<Result<TransactionCreateResult>> CreateTransactionAsync(
        SeededPortfolio seeded,
        string key,
        CreateTransactionRequest request,
        IPortfolioFundingService? funding = null)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, seeded.UserId, funding);
        return await service.CreateAsync(seeded.PortfolioId, request, key, CancellationToken.None);
    }

    private TransactionService CreateService(
        EquityLensDbContext db,
        Guid userId,
        IPortfolioFundingService? funding = null)
    {
        return new TransactionService(
            db,
            new TestCurrentUserContext(userId),
            new PortfolioRepository(db),
            new TransactionRepository(db),
            new PortfolioHoldingRepository(db),
            new SecurityRepository(db),
            funding ?? new PortfolioFundingService(db));
    }

    private async Task<SeededPortfolio> SeedPortfolioAsync(
        bool includeDividend = false,
        bool includeSecondSecurity = false)
    {
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var secondSecurityId = includeSecondSecurity ? Guid.NewGuid() : (Guid?)null;
        await using var db = CreateDbContext();
        db.Users.Add(new AppUser
        {
            Id = userId,
            Email = $"{userId:N}@integration.test",
            DisplayName = "Integration User",
            Role = "User",
            IsActive = true,
        });
        db.Portfolios.Add(new Portfolio
        {
            Id = portfolioId,
            OwnerUserId = userId,
            Name = $"Integration Portfolio {portfolioId:N}",
            BaseCurrency = "TWD",
            IsActive = true,
        });
        db.Securities.Add(CreateSecurity(securityId, "T" + securityId.ToString("N")[..8]));
        if (secondSecurityId.HasValue)
            db.Securities.Add(CreateSecurity(secondSecurityId.Value, "T" + secondSecurityId.Value.ToString("N")[..8]));
        if (includeDividend)
        {
            db.CashDividendEvents.Add(new CashDividendEvent
            {
                Id = Guid.NewGuid(),
                SecurityId = securityId,
                ExDividendDate = new DateOnly(2025, 1, 10),
                PaymentDate = new DateOnly(2025, 1, 20),
                CashAmountPerShare = 2m,
                Currency = "TWD",
                Source = "Integration",
                SourceKey = $"{securityId:N}:2025-01-10",
            });
        }
        await db.SaveChangesAsync();
        return new SeededPortfolio(userId, portfolioId, securityId, secondSecurityId);
    }

    private static Security CreateSecurity(Guid id, string ticker) => new()
    {
        Id = id,
        Ticker = ticker,
        Exchange = "TWSE",
        Name = "Integration Security",
        Currency = "TWD",
        IsActive = true,
    };

    private static CreateTransactionRequest BuyRequest(
        Guid securityId,
        decimal quantity = 100m,
        decimal price = 10m,
        decimal fee = 0m,
        DateOnly? date = null) => new(
            securityId,
            "BUY",
            quantity,
            price,
            fee,
            date ?? new DateOnly(2025, 1, 1),
            null);

    private EquityLensDbContext CreateDbContext()
    {
        return new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseNpgsql(_fixture.ConnectionString!, options => options.UseVector())
            .Options
            .CreateContext();
    }

    private void RequirePostgres()
    {
        if (string.IsNullOrWhiteSpace(_fixture.ConnectionString))
            throw Xunit.Sdk.SkipException.ForSkip("Set EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING to run PostgreSQL integration tests.");
    }

    private sealed record SeededPortfolio(Guid UserId, Guid PortfolioId, Guid SecurityId, Guid? SecondSecurityId);
    private sealed record InvalidRow(string Type, decimal Quantity, decimal Price, decimal Fee);

    private sealed class TestCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid UserId { get; } = userId;
        public string Email => "integration@test.local";
        public string DisplayName => "Integration User";
        public bool IsAuthenticated => true;
    }

    private sealed class ThrowingFundingService : IPortfolioFundingService
    {
        public Task RebuildImplicitFundingAsync(Guid portfolioId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Injected funding failure."));
    }
}

public sealed class PostgresTransactionDatabaseFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private string? _databaseName;
    private string? _adminConnectionString;

    public async Task InitializeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
        };
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = "postgres",
        };
        _adminConnectionString = adminBuilder.ConnectionString;
        _databaseName = $"equitylens_tx_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = _databaseName,
            Pooling = false,
        };
        ConnectionString = testBuilder.ConnectionString;
        await using var db = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseNpgsql(ConnectionString, options => options.UseVector())
            .Options
            .CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        ConnectionString = null;
        if (_databaseName is null || _adminConnectionString is null)
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }
}

internal static class DbContextOptionsExtensions
{
    public static EquityLensDbContext CreateContext(this DbContextOptions<EquityLensDbContext> options) =>
        new(options);
}
