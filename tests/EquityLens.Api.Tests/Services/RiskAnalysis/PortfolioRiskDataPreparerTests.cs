using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Tests.Infrastructure;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class PortfolioRiskDataPreparerTests
{
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly Guid FirstSecurity = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid SecondSecurity = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task PricePolicies_FormalRiskAllowsRawClose_BacktestRequiresCompleteAdjustedClose()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 360);
        db.MarketPrices.First(x => x.SecurityId == FirstSecurity).AdjustedClose = null;
        await db.SaveChangesAsync();
        var preparer = CreatePreparer(db);

        var formal = await preparer.PreparePortfolioRiskAsync(portfolio.Id, From, From.AddDays(360), 1, .95m, 1000,
            portfolio.OwnerUserId, default);
        var backtest = await new RiskBacktestInputProvider(preparer).PreparePortfolioRiskBacktestInputAsync(
            portfolio.Id, From, From.AddDays(360), portfolio.OwnerUserId, default);
        var monteCarlo = await preparer.PrepareMonteCarloAsync(portfolio.Id, portfolio.OwnerUserId, default);

        Assert.True(formal.IsSuccess);
        Assert.Equal(100m, formal.Value!.AlignedPrices[FirstSecurity][0]);
        Assert.Equal("risk.incomplete_adjusted_close", backtest.ErrorCode);
        Assert.True(monteCarlo.IsSuccess);
        Assert.NotNull(monteCarlo.Value!.Data);
    }

    [Fact]
    public async Task FormalRisk_PreservesAlignedDatesWeightsAndUnnormalizedTargetWeights()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 21);
        db.MarketPrices.Remove(db.MarketPrices.First(x => x.SecurityId == SecondSecurity && x.PriceTime == From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var preparer = CreatePreparer(db);
        var targetWeights = new Dictionary<Guid, decimal> { [FirstSecurity] = 1.3m, [SecondSecurity] = 0m };

        var result = await preparer.PreparePortfolioRiskAsync(portfolio.Id, From, From.AddDays(21), 1, .95m, 1000,
            portfolio.OwnerUserId, default, targetWeights: targetWeights);

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.Equal(20, data.CommonDates.Count);
        Assert.Equal(From.AddDays(1), data.CommonDates[0]);
        Assert.Equal(19, data.PortfolioReturns.Count);
        Assert.All(data.AlignedPrices.Values, series => Assert.Equal(20, series.Count));
        Assert.Equal(1.3m, data.Weights[FirstSecurity]);
        Assert.Equal(0m, data.Weights[SecondSecurity]);
        var prices = data.AlignedPrices[FirstSecurity];
        Assert.Equal(1.3m * (decimal)Math.Log((double)(prices[1] / prices[0])), data.PortfolioReturns[0]);
    }

    [Fact]
    public async Task MissingHoldingPrices_FormalRiskContinues_MonteCarloReportsUnavailable()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 21);
        db.MarketPrices.RemoveRange(db.MarketPrices.Where(x => x.SecurityId == SecondSecurity));
        await db.SaveChangesAsync();
        var preparer = CreatePreparer(db);

        var formal = await preparer.PreparePortfolioRiskAsync(portfolio.Id, From, From.AddDays(21), 1, .95m, 1000,
            portfolio.OwnerUserId, default);
        var monteCarlo = await preparer.PrepareMonteCarloAsync(portfolio.Id, portfolio.OwnerUserId, default);

        Assert.True(formal.IsSuccess);
        Assert.Single(formal.Value!.PricedHoldings);
        Assert.Equal(1m, formal.Value.Weights[FirstSecurity]);
        Assert.True(monteCarlo.IsSuccess);
        Assert.Null(monteCarlo.Value!.Data);
        Assert.Equal("所有持倉都需要最新有效價格，才能模擬共同投組路徑。", monteCarlo.Value.InsufficientMessage);
    }

    [Fact]
    public async Task Backtest_PreservesSortedAssetOrderAndReturnDates()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 360);
        var provider = new RiskBacktestInputProvider(CreatePreparer(db));
        var result = await provider.PreparePortfolioRiskBacktestInputAsync(portfolio.Id, From, From.AddDays(360), portfolio.OwnerUserId, default);

        Assert.True(result.IsSuccess);
        var input = result.Value!;
        Assert.Equal(359, input.ReturnDates.Count);
        Assert.Equal(From.AddDays(1), input.ReturnDates[0]);
        var expectedSecondReturn = (decimal)Math.Log((double)(200.6m / 200.5m));
        Assert.Equal(expectedSecondReturn, input.AssetReturns[0][0]);
        Assert.Equal(input.AssetReturns[0][0] * input.Weights[0] + input.AssetReturns[1][0] * input.Weights[1], input.PortfolioReturns[0]);
    }

    [Fact]
    public async Task AllPreparationModes_RejectAnotherUsersPortfolioWithoutWrites()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 21);
        var preparer = CreatePreparer(db);
        var otherUser = Guid.NewGuid();
        var formal = await preparer.PreparePortfolioRiskAsync(portfolio.Id, From, From.AddDays(21), 1, .95m, 1000, otherUser, default);
        var backtest = await preparer.PreparePortfolioRiskBacktestInputAsync(portfolio.Id, From, From.AddDays(21), otherUser, default);
        var monteCarlo = await preparer.PrepareMonteCarloAsync(portfolio.Id, otherUser, default);

        Assert.Equal("portfolio.not_found", formal.ErrorCode);
        Assert.Equal("portfolio.not_found", backtest.ErrorCode);
        Assert.Equal("portfolio.not_found", monteCarlo.ErrorCode);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static PortfolioRiskDataPreparer CreatePreparer(EquityLensDbContext db) => new(
        new PortfolioRepository(db), new MarketPriceRepository(db), new IdentityExchangeRates());

    private static async Task<Portfolio> SeedAsync(EquityLensDbContext db, int priceCount)
    {
        var first = new Security { Id = FirstSecurity, Ticker = "A", Name = "First", Currency = "TWD" };
        var second = new Security { Id = SecondSecurity, Ticker = "B", Name = "Second", Currency = "TWD" };
        var portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Fixture", BaseCurrency = "TWD",
            Holdings =
            [
                new PortfolioHolding { Id = Guid.NewGuid(), Security = first, Quantity = 10, CostCurrency = "TWD" },
                new PortfolioHolding { Id = Guid.NewGuid(), Security = second, Quantity = 20, CostCurrency = "TWD" }
            ]
        };
        db.Portfolios.Add(portfolio);
        for (var i = 0; i < priceCount; i++)
        {
            var date = From.AddDays(i).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), Security = first, PriceTime = date, Close = 100m + i * .2m, AdjustedClose = 100.5m + i * .2m });
            db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), Security = second, PriceTime = date, Close = 200m + i * .1m, AdjustedClose = 200.5m + i * .1m });
        }
        await db.SaveChangesAsync();
        return portfolio;
    }

    private sealed class IdentityExchangeRates : IExchangeRateService
    {
        public Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(amount);
        public Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(1m);
    }
}
