using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Tests.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        var expectedPortfolio = input.Weights[0] * (200.6m / 200.5m - 1m) + input.Weights[1] * (100.7m / 100.5m - 1m);
        Assert.Equal(expectedPortfolio, input.PortfolioReturns[0]);
        Assert.NotNull(input.Assets);
        Assert.Equal(new[] { SecondSecurity, FirstSecurity }, input.Assets.Select(asset => asset.SecurityId));
        Assert.Equal(new[] { "B", "A" }, input.Assets.Select(asset => asset.Ticker));
        Assert.Equal(new[] { "金融保險業", "電子工業" }, input.Assets.Select(asset => asset.Industry));
        Assert.Equal(new[] { "金融", "半導體" }, input.Assets.Select(asset => asset.RawIndustry));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(input, options);
        var restored = JsonSerializer.Deserialize<RiskBacktestEngineInput>(json, options)!;
        Assert.NotNull(restored.Assets);
        Assert.Equal(input.Assets.ToArray(), restored.Assets.ToArray());
        Assert.Equal(input.Weights, restored.Weights);
        Assert.Equal(input.AssetReturns[0], restored.AssetReturns[0]);
        db.Securities.Single(x => x.Id == FirstSecurity).Industry = "已變更";
        await db.SaveChangesAsync();
        Assert.Equal("電子工業", input.Assets[1].Industry);
        Assert.Equal("半導體", input.Assets[1].RawIndustry);
        Assert.Equal("電子工業", restored.Assets[1].Industry);
        Assert.Equal("半導體", restored.Assets[1].RawIndustry);

        var legacy = JsonNode.Parse(json)!;
        legacy.AsObject().Remove("assets");
        Assert.Null(JsonSerializer.Deserialize<RiskBacktestEngineInput>(legacy.ToJsonString(), options)!.Assets);
    }

    [Fact]
    public async Task Backtest_PortfolioReturnsSatisfyCrossLanguageSimpleReturnContract()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 360);
        var input = (await CreatePreparer(db).PreparePortfolioRiskBacktestInputAsync(
            portfolio.Id, From, From.AddDays(360), portfolio.OwnerUserId, default)).Value!;

        // Python RiskBacktestInput rejects payloads violating Σ w·(exp(r) − 1) beyond 1e-8.
        for (var day = 0; day < input.PortfolioReturns.Count; day++)
        {
            var expected = input.Weights
                .Select((weight, asset) => (double)weight * (Math.Exp((double)input.AssetReturns[asset][day]) - 1d))
                .Sum();
            Assert.InRange((double)input.PortfolioReturns[day] - expected, -1e-12, 1e-12);
        }
    }

    [Fact]
    public async Task AllPreparationModes_CarrySuspendedDayForwardInsteadOfMergingReturns()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 360);
        var suspended = From.AddDays(5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        db.MarketPrices.Remove(db.MarketPrices.Single(x => x.SecurityId == SecondSecurity && x.PriceTime == suspended));
        await db.SaveChangesAsync();
        var preparer = CreatePreparer(db);

        var formal = (await preparer.PreparePortfolioRiskAsync(portfolio.Id, From, From.AddDays(360), 1, .95m, 1000,
            portfolio.OwnerUserId, default)).Value!;
        var backtest = (await preparer.PreparePortfolioRiskBacktestInputAsync(
            portfolio.Id, From, From.AddDays(360), portfolio.OwnerUserId, default)).Value!;
        var monteCarlo = (await preparer.PrepareMonteCarloAsync(portfolio.Id, portfolio.OwnerUserId, default)).Value!;

        Assert.Equal(360, formal.CommonDates.Count);
        Assert.Contains(From.AddDays(5), formal.CommonDates);
        Assert.Equal(formal.AlignedPrices[SecondSecurity][4], formal.AlignedPrices[SecondSecurity][5]);
        Assert.Equal(360, monteCarlo.CommonTradingDays);
        Assert.Equal(359, backtest.ReturnDates.Count);
        var second = Array.IndexOf(backtest.Assets!.Select(a => a.SecurityId).ToArray(), SecondSecurity);
        var first = 1 - second;
        // Day 5 is a zero-return carry-forward; the resumption day reflects the full two-day move.
        Assert.Equal(0m, backtest.AssetReturns[second][4]);
        Assert.Equal((decimal)Math.Log((double)(201.1m / 200.9m)), backtest.AssetReturns[second][5]);
        Assert.NotEqual(0m, backtest.AssetReturns[first][4]);
    }

    [Fact]
    public async Task Backtest_BlankClassificationUsesUnclassified()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedAsync(db, 360);
        var security = db.Securities.Single(x => x.Id == FirstSecurity);
        security.Industry = " ";
        security.Sector = null;
        await db.SaveChangesAsync();
        var result = await CreatePreparer(db).PreparePortfolioRiskBacktestInputAsync(
            portfolio.Id, From, From.AddDays(360), portfolio.OwnerUserId, default);
        Assert.Equal("未分類", result.Value!.Assets!.Single(x => x.SecurityId == FirstSecurity).Industry);
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
        var first = new Security { Id = FirstSecurity, Ticker = "A", Name = "First", Currency = "TWD", Industry = "半導體", Sector = "科技" };
        var second = new Security { Id = SecondSecurity, Ticker = "B", Name = "Second", Currency = "TWD", Industry = " ", Sector = "金融" };
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
