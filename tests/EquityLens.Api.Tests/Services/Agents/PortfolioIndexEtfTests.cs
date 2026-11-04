using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioIndexEtfTests
{
    private static PortfolioScenarioSnapshot Snapshot(int days = 130)
    {
        var dates = Enumerable.Range(0, days).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).ToArray();
        return new(Guid.NewGuid(), DateTime.UtcNow, "TWD", dates[0], dates[^1], dates,
            [new("2327", 80, 100, dates.Select((_, i) => 100m + i % 3).ToArray()),
             new("2330", 20, 100, dates.Select((_, i) => 100m + i % 3).ToArray())], 0, [], PriceBasis: "AdjustedClose");
    }
    private static PortfolioScenarioRequest Request(string policy = "index_etf", string? ticker = "0050") =>
        new("comparison", new Dictionary<string, decimal> { ["2327"] = .6m }, policy, true, ticker);

    [Fact]
    public void NewEtfAndCashUseIdenticalDatesBaselineAndConserveNav()
    {
        var original = Snapshot();
        var s = original with { Assets = [..original.Assets, new("0050", 0, 50, original.Dates.Select(_ => 50m).ToArray(), "元大台灣50", true)] };
        var comparison = PortfolioScenarioCalculator.Calculate(s, [Request("cash", null), Request() with { Name = "ETF" }]);
        var cash = comparison.Scenarios[0]; var etf = comparison.Scenarios[1];
        Assert.Equal(0, comparison.Baseline!.Weights["0050"]);
        Assert.Equal(.2m, etf.Weights["0050"]); Assert.Equal(40, etf.Quantities["0050"]);
        Assert.Equal(20, etf.Quantities["2330"]); Assert.Equal(60, etf.Quantities["2327"]);
        Assert.Equal(0, etf.CashWeight); Assert.Equal(.2m, cash.CashWeight);
        Assert.Equal(1, etf.Weights.Values.Sum() + etf.CashWeight);
        Assert.Equal(10000, s.Assets.Sum(a => a.ValuationPrice * etf.Quantities[a.Ticker]));
        Assert.Equal(cash.Metrics!.AnnualizedVolatility, etf.Metrics!.AnnualizedVolatility); // constant ETF equals zero-return cash
        Assert.InRange(Math.Abs(etf.Metrics.AnnualizedVolatility - comparison.Baseline.Metrics!.AnnualizedVolatility * .8m), 0, .00000001m);
        Assert.Equal(PortfolioScenarioCalculator.Calculate(original, [Request("cash", null)]).Baseline!.Metrics!.AnnualizedVolatility, comparison.Baseline.Metrics.AnnualizedVolatility);
        Assert.Equal(original.Dates, comparison.Snapshot!.Dates);
        Assert.Contains("轉入 0050", PortfolioQualityGateNodeHandler.ScenarioSummary(comparison));
        Assert.Contains(comparison.Warnings, x => x.Contains("未穿透"));
        Assert.Equal(80, original.Assets[0].Quantity); // pure calculation
    }

    [Fact]
    public void IndexEtfCanWorsenRiskAndCannotBeAssumedBetter()
    {
        var original = Snapshot();
        var s = original with { Assets = [..original.Assets, new("0050", 0, 50, original.Dates.Select((_, i) => 50m + 20 * (i % 3)).ToArray(), "ETF", true)] };
        var c = PortfolioScenarioCalculator.Calculate(s, [Request()]);
        Assert.True(c.Scenarios[0].Metrics!.AnnualizedVolatility > c.Baseline!.Metrics!.AnnualizedVolatility);
        Assert.True(c.Scenarios[0].DifferenceFromBaseline!.AnnualizedVolatility > 0);
    }

    [Fact]
    public void CandidateIsNotAHoldingOrAnArbitraryNewStock()
    {
        var original = Snapshot();
        var s = original with { Assets = [..original.Assets, new("0050", 0, 50, original.Dates.Select(_ => 50m).ToArray(), "ETF", true)] };
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(s, [Request("holding")]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(s, [Request() with { TargetWeights = new Dictionary<string, decimal> { ["0050"] = 0 }, TransferToTicker = "006208" }]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(s, [Request(ticker: "2330")]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(original, [Request()]));
        Assert.Throws<AgentWorkflowQueryException>(() => PortfolioScenarioCalculator.Calculate(s with { Assets = s.Assets.Select(a => a with { IsEligibleIndexEtf = false }).ToArray() }, [Request()]));
    }

    [Theory]
    [InlineData("absent", false)]
    [InlineData("duplicate-security", false)]
    [InlineData("portfolio-currency", false)]
    [InlineData("complete", true)]
    [InlineData("missing", false)]
    [InlineData("unadjusted", false)]
    [InlineData("duplicate", false)]
    [InlineData("currency", false)]
    [InlineData("inactive", false)]
    [InlineData("stock", false)]
    [InlineData("exchange", false)]
    [InlineData("short", false)]
    [InlineData("holding-unadjusted", false)]
    public async Task CandidateReaderRequiresApprovedMetadataAndEveryFrozenDate(string mode, bool ready)
    {
        await using var db = new RefactorTestDbContext(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var s = Snapshot(mode == "short" ? 120 : 130);
        if (mode == "holding-unadjusted") s = s with { PriceBasis = "MixedOrClose" };
        var etf = new Security { Id = Guid.NewGuid(), Ticker = "0050", Exchange = mode == "exchange" ? "NYSE" : "TWSE", Name = "ETF",
            AssetType = mode == "stock" ? "Stock" : "ETF", Currency = mode == "currency" ? "USD" : "TWD", IsActive = mode != "inactive" };
        if (mode == "portfolio-currency") s = s with { Currency = "USD" };
        if (mode != "absent") db.Securities.Add(etf);
        if (mode == "duplicate-security") db.Securities.Add(new Security { Id = Guid.NewGuid(), Ticker = "0050", Exchange = "OTHER", Currency = "USD", AssetType = "Stock" });
        foreach (var d in s.Dates)
        {
            if (mode == "missing" && d == s.Dates[20]) continue;
            db.MarketPrices.Add(new() { Id = Guid.NewGuid(), SecurityId = etf.Id, Interval = "1d", PriceTime = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Close = 50,
                AdjustedClose = mode == "unadjusted" && d == s.Dates[20] ? null : 50 });
        }
        if (mode == "duplicate") db.MarketPrices.Add(new() { Id = Guid.NewGuid(), SecurityId = etf.Id, Interval = "1d", PriceTime = DateTime.SpecifyKind(s.Dates[20].ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc), Close = 50, AdjustedClose = 50 });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await PortfolioIndexEtfs.AttachAsync(db, s, default);
        var candidate = Assert.Single(result.IndexEtfCandidates!, c => c.Ticker == "0050");
        Assert.Equal(ready ? "Ready" : "Unavailable", candidate.Status);
        Assert.Equal(ready ? 3 : 2, result.Assets.Count);
        Assert.Equal(s.Dates, result.Dates); Assert.Equal(s.CapturedAtUtc, result.CapturedAtUtc);
        Assert.Equal(s.Assets[0], result.Assets[0]);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);
        Assert.Empty(await db.PortfolioHoldings.ToListAsync());
        Assert.Empty(await db.PortfolioTransactions.ToListAsync());
        if (ready) Assert.All(result.Assets.Single(a => a.Ticker == "0050").Prices, price => Assert.Equal(50, price));
        else {
            Assert.NotNull(candidate.Reason);
            var expected = mode switch {
                "absent" => "找不到證券資料", "duplicate-security" => "證券資料有重複（2 筆）",
                "inactive" => "未啟用", "exchange" => "交易所不符", "currency" or "portfolio-currency" => "幣別不符",
                "stock" => "AssetType 不是 ETF", "missing" or "unadjusted" => "缺還原收盤價 1 天",
                "duplicate" => "還原收盤價有重複（1 天）", "short" => "至少需要120", _ => "既有持股缺少完整還原收盤價"
            };
            Assert.Contains(expected, candidate.Reason);
        }
    }

    [Fact]
    public void EtfNameDigitsAreIdentifiersButUnsupportedReturnsRemainRejected()
    {
        var source = new JsonObject { ["indexEtfCandidates"] = JsonSerializer.SerializeToNode(PortfolioIndexEtfs.Universe, AgentNodeJson.SerializerOptions), ["weight"] = .2m };
        var facts = new[] { new LoopDataFact("P1", "ETF", "get_portfolio_snapshot", source) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("元大台灣50（0050）追蹤臺灣50，配置20% [P1]", ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new("元大台灣50，年化報酬99% [P1]", ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new("元大台灣50 [P2]", ["P2"]), facts));
    }

    [Fact]
    public async Task SnapshotToolExposesCandidatesWithoutHoldingsAndReusesPricesAfterDbChanges()
    {
        var s = Snapshot();
        var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped<EquityLensDbContext>(_ => new RefactorTestDbContext(options))
            .AddScoped<IPortfolioRiskMathInputProvider, PortfolioRiskMathInputProvider>().BuildServiceProvider();
        var owner = Guid.NewGuid();
        await using var db = new RefactorTestDbContext(options);
        var portfolio = new Portfolio { Id = s.PortfolioId, OwnerUserId = owner, Name = "test", BaseCurrency = "TWD" };
        db.Portfolios.Add(portfolio);
        foreach (var asset in s.Assets.Concat([new ScenarioAsset("0050", 0, 50, s.Dates.Select(_ => 50m).ToArray())]))
        {
            var security = new Security { Id = Guid.NewGuid(), Ticker = asset.Ticker, Name = asset.Ticker, Currency = "TWD", Exchange = "TWSE", AssetType = asset.Ticker == "0050" ? "ETF" : "Stock" };
            db.Securities.Add(security);
            if (asset.Quantity > 0) db.PortfolioHoldings.Add(new() { Id = Guid.NewGuid(), PortfolioId = portfolio.Id, SecurityId = security.Id, Quantity = asset.Quantity, AverageCost = 100 });
            for (var i = 0; i < s.Dates.Count; i++) db.MarketPrices.Add(new() { Id = Guid.NewGuid(), SecurityId = security.Id, Interval = "1d",
                PriceTime = DateTime.SpecifyKind(s.Dates[i].ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Close = asset.Prices[i], AdjustedClose = asset.Prices[i] });
        }
        await db.SaveChangesAsync();
        var board = JsonNode.Parse(PortfolioDiagnosisWorkflowDefinitionProvider.CreateInitialBlackboardJson(s.PortfolioId, s.From, s.To, null))!.AsObject();
        board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(s.PortfolioId, "test", "TWD", s.From, s.To, 2), AgentNodeJson.SerializerOptions);
        var scope = new LoopToolScope(owner, new("", "可以給我優化建議嗎？"), [], false, DomainContext: board);
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var result = await tools.InvokeAsync(scope, new("snapshot", "get_portfolio_snapshot", new()), default);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Data!["holdings"]!.AsArray().Count);
        Assert.Equal("Ready", result.Data["indexEtfCandidates"]![0]!["status"]!.ToString());
        foreach (var item in result.BlackboardPatch!) board[item.Key] = item.Value?.DeepClone();
        var frozen = board[PortfolioLoopTools.SnapshotKey]!.ToJsonString();
        var etfId = (await db.Securities.SingleAsync(x => x.Ticker == "0050")).Id;
        foreach (var price in await db.MarketPrices.Where(x => x.SecurityId == etfId).ToListAsync()) { price.Close = 200; price.AdjustedClose = 200; }
        await db.SaveChangesAsync();
        var repeated = await tools.InvokeAsync(scope, new("again", "get_portfolio_snapshot", new()), default);
        Assert.Equal(frozen, repeated.BlackboardPatch![PortfolioLoopTools.SnapshotKey]!.ToJsonString());
        var comparison = await tools.InvokeAsync(scope, new("simulate", "simulate_weight_scenario", new() {
            ["scenarios"] = JsonSerializer.SerializeToNode(new[] { Request("cash", null), Request() with { Name = "ETF" } }, AgentNodeJson.SerializerOptions) }), default);
        Assert.Null(comparison.Error);
        Assert.Equal(2, comparison.Data!["scenarios"]!.AsArray().Count);
        var snapshot = comparison.BlackboardPatch![PortfolioScenarioCalculator.ResultKey]!["snapshot"]!.Deserialize<PortfolioScenarioSnapshot>(AgentNodeJson.SerializerOptions)!;
        Assert.Equal(50, snapshot.Assets.Single(a => a.Ticker == "0050").ValuationPrice);
        Assert.Equal(2, await db.PortfolioHoldings.CountAsync());
        Assert.Equal(80, (await db.PortfolioHoldings.Include(h => h.Security).SingleAsync(h => h.Security.Ticker == "2327")).Quantity);
        Assert.Empty(await db.PortfolioTransactions.ToListAsync());
    }

    [Fact]
    public async Task ToolUsesPersistedEtfPricesAndChecksOwnerBeforeReturningData()
    {
        var s = Snapshot();
        s = s with { Assets = [..s.Assets, new("0050", 0, 50, s.Dates.Select(_ => 50m).ToArray(), "ETF", true)] };
        var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped<EquityLensDbContext>(_ => new RefactorTestDbContext(options)).BuildServiceProvider();
        var owner = Guid.NewGuid();
        await using (var db = new RefactorTestDbContext(options))
        { db.Portfolios.Add(new() { Id = s.PortfolioId, OwnerUserId = owner, Name = "test" }); await db.SaveChangesAsync(); }
        var board = new JsonObject { [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(s.PortfolioId, "test", "TWD", s.From, s.To, 2), AgentNodeJson.SerializerOptions),
            [PortfolioLoopTools.SnapshotKey] = JsonSerializer.SerializeToNode(s, AgentNodeJson.SerializerOptions) };
        var scope = new LoopToolScope(owner, new("", "優化"), [], false, DomainContext: board);
        var call = new LoopToolCall("etf", "simulate_weight_scenario", new() { ["scenarios"] = JsonSerializer.SerializeToNode(new[] { Request() }, AgentNodeJson.SerializerOptions) });
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var r = await tools.InvokeAsync(scope, call, default);
        Assert.Null(r.Error); Assert.Equal(.2m, r.Data!["scenarios"]![0]!["weights"]!["0050"]!.GetValue<decimal>());
        Assert.DoesNotContain("prices", r.Data.ToJsonString());
        Assert.Equal("portfolio_not_found", (await tools.InvokeAsync(scope with { UserId = Guid.NewGuid() }, call, default)).Error);
        Assert.Equal("UnexpectedArgument", tools.Validate(scope, call with { Arguments = new JsonObject { ["prices"] = new JsonArray(1, 2) } }));
        await using var verify = new RefactorTestDbContext(options);
        Assert.Empty(await verify.PortfolioHoldings.ToListAsync()); Assert.Empty(await verify.PortfolioTransactions.ToListAsync());
    }
}
