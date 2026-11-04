using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioLoopToolsTests
{
    [Theory]
    [InlineData("recorded", "Available", 731)]
    [InlineData("zero", "Available", 0)]
    [InlineData("missing", "Unavailable", null)]
    [InlineData("pending", "Unavailable", null)]
    [InlineData("foreign-flow", "Unavailable", null)]
    [InlineData("foreign-trade", "Unavailable", null)]
    [InlineData("unknown-flow", "Unavailable", null)]
    [InlineData("derived", "Available", 731)]
    public async Task SnapshotCashUsesOwnedRecordedLedger_AndNeverAssumesMissingMeansZero(string mode, string status, int? expected)
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped<EquityLensDbContext>(_ => new RefactorTestDbContext(options)).BuildServiceProvider();
        var id = Guid.NewGuid(); var owner = Guid.NewGuid(); var otherId = Guid.NewGuid();
        var date = new DateOnly(2025, 5, 1);
        await using var db = new RefactorTestDbContext(options);
        db.Portfolios.AddRange(new Portfolio { Id = id, OwnerUserId = owner, Name = "cash" },
            new Portfolio { Id = otherId, OwnerUserId = Guid.NewGuid(), Name = "other" });
        var security = new Security { Id = Guid.NewGuid(), Ticker = "2330", Currency = mode == "foreign-trade" ? "USD" : "TWD" };
        db.Securities.Add(security);
        if (mode != "missing")
        {
            db.PortfolioCashFlows.AddRange(
                new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Deposit", Amount = mode == "zero" ? 1269 : 2000,
                    Currency = mode == "foreign-flow" ? "USD" : "TWD", EffectiveDate = date, Status = mode == "pending" ? "Pending" : "Posted", IsSystemDerived = mode == "derived" },
                new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = mode == "unknown-flow" ? "Unknown" : "Withdrawal", Amount = 500, EffectiveDate = date, Status = "Confirmed" },
                new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Dividend", Amount = 50, EffectiveDate = date, Status = "Applied" },
                new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "DividendTax", Amount = 5, EffectiveDate = date, Status = "Posted" },
                new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Fee", Amount = 7, EffectiveDate = date, Status = "Posted" });
            db.PortfolioTransactions.AddRange(
                new() { Id = Guid.NewGuid(), PortfolioId = id, SecurityId = security.Id, TransactionType = "BUY", Quantity = 10, Price = 100, Fee = 10, TransactionDate = date },
                new() { Id = Guid.NewGuid(), PortfolioId = id, SecurityId = security.Id, TransactionType = "SELL", Quantity = 2, Price = 103, Fee = 3, TransactionDate = date });
        }
        // Neither other owners nor future / skipped / scheduled activity may leak into the balance.
        db.PortfolioCashFlows.AddRange(
            new() { Id = Guid.NewGuid(), PortfolioId = otherId, FlowType = "Deposit", Amount = 888888, EffectiveDate = date, Status = "Posted" },
            new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Deposit", Amount = 999999, EffectiveDate = date.AddDays(1), Status = "Posted" },
            new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Deposit", Amount = 999999, EffectiveDate = date, Status = "Skipped" },
            new() { Id = Guid.NewGuid(), PortfolioId = id, FlowType = "Deposit", Amount = 999999, EffectiveDate = date, Status = "Scheduled" });
        db.PortfolioTransactions.Add(new() { Id = Guid.NewGuid(), PortfolioId = id, SecurityId = security.Id, TransactionType = "BUY", Quantity = 999, Price = 100, TransactionDate = date.AddDays(1) });
        await db.SaveChangesAsync();
        var beforeFlows = await db.PortfolioCashFlows.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var beforeTrades = await db.PortfolioTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var inputs = new PortfolioRiskMathInputs("Portfolio", id, null, date, date, [], [], [], [], [], [], [], [], 0, "test", []);
        var board = new JsonObject {
            [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(id, "cash", "TWD", date, date, 0), AgentNodeJson.SerializerOptions),
            [AgentBlackboardKeys.MathInputs] = JsonSerializer.SerializeToNode(inputs, AgentNodeJson.SerializerOptions),
            [PortfolioLoopTools.SnapshotKey] = JsonSerializer.SerializeToNode(new PortfolioScenarioSnapshot(id, DateTime.UtcNow, "TWD", date, date, [], [], 0, []), AgentNodeJson.SerializerOptions) };
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var scope = new LoopToolScope(owner, new("", "現金是多少"), [], false, DomainContext: board);
        var result = await tools.InvokeAsync(scope, new("snapshot", "get_portfolio_snapshot", new()), default);
        Assert.Null(result.Error);
        var cash = result.Data!["accountCash"];
        Assert.NotNull(cash);
        Assert.Equal(status, cash!["status"]!.GetValue<string>());
        Assert.Equal(date.ToString("yyyy-MM-dd"), cash["asOf"]!.ToString());
        Assert.Equal("RecordedLedger", cash["basis"]!.GetValue<string>());
        Assert.Equal("TWD", cash["currency"]!.GetValue<string>());
        if (expected.HasValue)
        {
            Assert.Equal((decimal)expected.Value, cash["balance"]!.GetValue<decimal>());
            var facts = new[] { new LoopDataFact("P1", "snapshot", "get_portfolio_snapshot", result.Data) };
            Assert.Empty(PortfolioQualityGateNodeHandler.Check(new($"系統帳簿現金餘額 {expected.Value} 元 [P1]", ["P1"]), facts));
        }
        else { Assert.Null(cash["balance"]); Assert.False(string.IsNullOrWhiteSpace(cash["reason"]?.ToString())); }
        Assert.Equal(mode == "derived", cash["includesSystemDerivedFunding"]!.GetValue<bool>());
        Assert.Equal(JsonSerializer.Serialize(beforeFlows), JsonSerializer.Serialize(await db.PortfolioCashFlows.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
        Assert.Equal(JsonSerializer.Serialize(beforeTrades), JsonSerializer.Serialize(await db.PortfolioTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
        var denied = await tools.InvokeAsync(scope with { UserId = Guid.NewGuid() }, new("snapshot", "get_portfolio_snapshot", new()), default);
        Assert.Equal("portfolio_not_found", denied.Error); Assert.Null(denied.Data);
    }

    [Fact]
    public void RiskDisplayKeepsSourceSpecificRounding()
    {
        var metrics = new PortfolioRiskMetrics(null, null, null, null, .2453m, null, null, null,
            [new("9001", .8m, .24m, .99774663m)]);
        var display = PortfolioLoopTools.DisplayMetrics(metrics);
        Assert.Equal("99.77%", display["contributions"]![0]!["componentRiskShare"]!.GetValue<string>());
        Assert.Equal("24.53%", display["portfolioVolatility"]!.GetValue<string>());
        var facts = new[] { new LoopDataFact("P1", "risk", "run_risk_analysis", new JsonObject {
            ["display"] = display, ["componentRiskShare"] = .99774663m }) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("風險貢獻 99.77% [P1]", ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new("風險貢獻 99.78% [P1]", ["P1"]), facts));
    }

    [Fact]
    public void OnlyTaskToolsAreExposed_AndAllMathOperationsAreDenied()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var scope = new LoopToolScope(Guid.NewGuid(), new("", "診斷投組"), [], false);
        Assert.Equal(6, tools.Visible(scope).Count);
        foreach (var operation in PortfolioRiskMathCapabilities.All)
        {
            Assert.DoesNotContain(tools.Visible(scope), x => x.Name == operation.Id);
            Assert.Equal("ToolNotVisible", tools.Validate(scope, new("c", operation.Id, new())));
        }
        Assert.All(tools.Visible(scope), x => Assert.False(x.Parameters["additionalProperties"]!.GetValue<bool>()));
    }

    [Theory]
    [InlineData("get_portfolio_snapshot", "prices")]
    [InlineData("get_risk_report", "portfolioId")]
    [InlineData("run_risk_analysis", "returns")]
    [InlineData("get_performance_attribution", "holdings")]
    [InlineData("simulate_weight_scenario", "covariance")]
    [InlineData("get_risk_profile", "riskTolerance")]
    public void AllToolsRejectCallerSuppliedData(string name, string key)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        Assert.Equal("UnexpectedArgument", tools.Validate(new(Guid.NewGuid(), new("", "test"), [], false), new("c", name, new() { [key] = new JsonArray(1, 2) })));
    }

    [Theory]
    [InlineData("holding", "2330")]
    [InlineData("index_etf", "0050")]
    public void ExplicitHoldingDestinationCannotBeReplacedWithCash(string policy, string destination)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var request = new PortfolioScenarioRequest("轉入", new Dictionary<string, decimal> { ["2327"] = .3m }, policy, true, destination);
        var board = new JsonObject { [AgentBlackboardKeys.RoutingContext] = new JsonObject {
            ["scenarios"] = JsonSerializer.SerializeToNode(new[] { request }, AgentNodeJson.SerializerOptions) }, [PortfolioLoopTools.SnapshotKey] = new JsonObject() };
        var scope = new LoopToolScope(Guid.NewGuid(), new("", "移到台積電"), [], false, DomainContext: board);
        var substituted = request with { RemainderPolicy = "cash", TransferToTicker = null };
        Assert.Equal("RequestedScenarioMismatch", tools.Validate(scope, new("s", "simulate_weight_scenario", new() {
            ["scenarios"] = JsonSerializer.SerializeToNode(new[] { substituted }, AgentNodeJson.SerializerOptions) })));
    }

    [Theory]
    [InlineData("get_portfolio_snapshot")]
    [InlineData("get_risk_report")]
    [InlineData("run_risk_analysis")]
    [InlineData("get_performance_attribution")]
    [InlineData("get_risk_profile")]
    public async Task ReadToolsUseOwnedData_AndNeverWriteHoldings(string name)
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped<EquityLensDbContext>(_ => new RefactorTestDbContext(options))
            .AddScoped<IPortfolioRiskMathInputProvider, PortfolioRiskMathInputProvider>()
            .AddScoped<IPortfolioRiskMathExecutor, PortfolioRiskMathExecutor>()
            .AddScoped<IPortfolioRiskRunResolver, CachedRisk>()
            .AddScoped<IPortfolioValuationService, Valuations>().BuildServiceProvider();
        var owner = Guid.NewGuid(); var id = Guid.NewGuid(); var security = Guid.NewGuid();
        var from = new DateOnly(2025, 1, 1); var to = from.AddDays(129);
        await using var db = new RefactorTestDbContext(options);
        db.Portfolios.Add(new() { Id = id, OwnerUserId = owner, Name = "test", Holdings = [new() { Id = Guid.NewGuid(), PortfolioId = id,
            SecurityId = security, Security = new() { Id = security, Ticker = "2330", Name = "台積電", Currency = "TWD" }, Quantity = 10, AverageCost = 90 }] });
        for (var i = 0; i < 130; i++) db.MarketPrices.Add(new() { Id = Guid.NewGuid(), SecurityId = security, Interval = "1d",
            PriceTime = DateTime.SpecifyKind(from.AddDays(i).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), Close = 100 + i % 7 });
        await db.SaveChangesAsync();
        var board = JsonNode.Parse(PortfolioDiagnosisWorkflowDefinitionProvider.CreateInitialBlackboardJson(id, from, to, null))!.AsObject();
        board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(id, "test", "TWD", from, to, 1), AgentNodeJson.SerializerOptions);
        db.Users.Add(new() { Id = owner }); await db.SaveChangesAsync();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var scope = new LoopToolScope(owner, new("", "test"), [], false, DomainContext: board);
        if (name == "run_risk_analysis")
        {
            var snapshot = await tools.InvokeAsync(scope, new("snapshot", "get_portfolio_snapshot", new()), default);
            Assert.Null(snapshot.Error);
            foreach (var item in snapshot.BlackboardPatch!) board[item.Key] = item.Value?.DeepClone();
        }
        var result = await tools.InvokeAsync(scope, new("call", name, name == "run_risk_analysis" ? new() { ["analysis"] = "Core" } : new()), default);
        Assert.Null(result.Error); Assert.NotNull(result.Data); Assert.NotNull(result.BlackboardPatch);
        if (name == "get_portfolio_snapshot") { Assert.NotNull(result.BlackboardPatch[PortfolioLoopTools.SnapshotKey]); Assert.Equal("台積電", Assert.Single(result.Data["holdings"]!.AsArray())!["securityName"]!.GetValue<string>()); }
        if (name == "get_risk_report") { Assert.Equal("Hit", result.Data["evidence"]!["cacheStatus"]!.GetValue<string>()); Assert.Equal(.2m, result.Data["metrics"]!["annualizedVolatility"]!.GetValue<decimal>()); }
        if (name == "run_risk_analysis") Assert.Equal(4, result.Data["results"]!.AsArray().Count);
        if (name == "get_performance_attribution") Assert.Equal("complete", result.Data["attribution"]!["coverageStatus"]!.GetValue<string>());
        if (name == "get_risk_profile") {
            Assert.True(result.Data["available"]!.GetValue<bool>());
            Assert.True(result.Data["isDefault"]!.GetValue<bool>());
            Assert.Equal("Balanced", result.Data["value"]!.ToString());
            Assert.Contains("預設，可在設定修改", result.Data["statement"]!.ToString());
            var user = await db.Users.SingleAsync(x => x.Id == owner);
            user.RiskPreference = "Conservative"; await db.SaveChangesAsync();
            var saved = await tools.InvokeAsync(scope, new("saved", name, new()), default);
            Assert.False(saved.Data!["isDefault"]!.GetValue<bool>());
            Assert.Equal("Conservative", saved.Data["value"]!.ToString());
            Assert.Equal("保守", saved.Data["label"]!.ToString());
        }
        Assert.Equal(10, (await db.PortfolioHoldings.SingleAsync()).Quantity);
        Assert.Empty(await db.PortfolioTransactions.ToListAsync());
    }

    [Fact]
    public void MissingDestinationHasActionableFeedback_AndDoesNotRejectValidSibling()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var scope = new LoopToolScope(Guid.NewGuid(), new("", "給我優化建議"), [], false,
            DomainContext: new() { [PortfolioLoopTools.SnapshotKey] = new JsonObject() });
        var bad = new PortfolioScenarioRequest("a", new Dictionary<string, decimal> { ["2327"] = .1m }, "holding");
        LoopToolCall Call(params PortfolioScenarioRequest[] scenarios) => new("s", "simulate_weight_scenario", new() {
            ["scenarios"] = JsonSerializer.SerializeToNode(scenarios, AgentNodeJson.SerializerOptions) });
        Assert.Contains("transferToTicker", tools.Validate(scope, Call(bad)));
        Assert.Null(tools.Validate(scope, Call(bad, bad with { Name = "b", RemainderPolicy = "cash" })));
    }

    private sealed class CachedRisk : IPortfolioRiskRunResolver
    {
        public Task<(PortfolioRiskEvidenceSnapshot Evidence, IReadOnlyList<JsonObject> Results)> ResolveAsync(PortfolioDiagnosisContext context, Guid userId, CancellationToken ct)
            => Task.FromResult<(PortfolioRiskEvidenceSnapshot, IReadOnlyList<JsonObject>)>((new([Guid.NewGuid()], "PersistedRiskRun", "Hit", context.To, "hash", "fixture", null,
                ["calculate-annualized-volatility"], [], [], false), [new() { ["operation"] = "calculate-annualized-volatility", ["value"] = .2m }]));
    }
    private sealed class Valuations : IPortfolioValuationService
    {
        public Task<Result<PortfolioValuationResponse>> GetValuationAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PortfolioValuationHistoryResponse>> GetValuationHistoryAsync(Guid id, DateOnly from, DateOnly to, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PortfolioValuationHistoryResponse>> GetValuationHistoryForUserAsync(Guid id, Guid user, DateOnly from, DateOnly to, CancellationToken ct)
            => Task.FromResult(Result<PortfolioValuationHistoryResponse>.Success(new(id, from, to, "TWD", [], .1m, BenchmarkReturn: .08m, ExcessReturn: .02m)));
    }

    [Fact]
    public async Task ScenarioToolReusesSavedSnapshotAndCalculator_AndEnforcesOwnership()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().AddScoped<EquityLensDbContext>(_ => new RefactorTestDbContext(options)).BuildServiceProvider();
        var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        await using (var db = new RefactorTestDbContext(options))
        { db.Portfolios.Add(new() { Id = id, OwnerUserId = owner, Name = "test" }); await db.SaveChangesAsync(); }
        var dates = Enumerable.Range(0, 130).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).ToArray();
        var snapshot = new PortfolioScenarioSnapshot(id, DateTime.UtcNow, "TWD", dates[0].AddDays(-7), dates[^1], dates,
            [new("2330", 10, 100, dates.Select((_, i) => 100m + i % 5).ToArray())], 1000, []);
        var requests = new[] { new PortfolioScenarioRequest("減碼", new Dictionary<string, decimal> { ["2330"] = .3m }) };
        var board = new JsonObject { [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(id, "test", "TWD", dates[0], dates[^1], 1), AgentNodeJson.SerializerOptions),
            [PortfolioLoopTools.SnapshotKey] = JsonSerializer.SerializeToNode(snapshot, AgentNodeJson.SerializerOptions) };
        var scope = new LoopToolScope(owner, new("", "test"), [], false, DomainContext: board);
        var call = new LoopToolCall("s", "simulate_weight_scenario", new() { ["scenarios"] = JsonSerializer.SerializeToNode(requests, AgentNodeJson.SerializerOptions) });
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var result = await tools.InvokeAsync(scope, call, default);
        Assert.Null(result.Error);
        var actual = result.BlackboardPatch![PortfolioScenarioCalculator.ResultKey]!.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions)!;
        var expected = PortfolioScenarioCalculator.Calculate(snapshot, requests);
        Assert.Equal(expected.InputHash, actual.InputHash);
        Assert.Equal(expected.Scenarios[0].Metrics!.AnnualizedVolatility, actual.Scenarios[0].Metrics!.AnnualizedVolatility);
        Assert.Equal(dates[0].ToString("yyyy-MM-dd"), result.Data!["period"]?["from"]?.ToString());
        Assert.Equal(dates[^1].ToString("yyyy-MM-dd"), result.Data!["period"]?["to"]?.ToString());
        Assert.Equal(dates[0].AddDays(-7).ToString("yyyy-MM-dd"), result.Data!["period"]?["requestedFrom"]?.ToString());
        Assert.Equal(dates[1].ToString("yyyy-MM-dd"), result.Data!["period"]?["returnFrom"]?.ToString());
        Assert.Equal(-.2m, result.Data!["scenarios"]![0]!["weightChanges"]!["2330"]!.GetValue<decimal>());
        Assert.Equal(.2m, result.Data!["scenarios"]![0]!["cashWeightChange"]!.GetValue<decimal>());
        Assert.Null(result.Data!["snapshot"]);
        Assert.DoesNotContain("prices", result.Data.ToJsonString());
        Assert.Null(board[PortfolioScenarioCalculator.ResultKey]);
        var partial = PortfolioLoopTools.CalculateAvailable(snapshot, [requests[0], new("bad", new Dictionary<string, decimal> { ["2330"] = .9m })]);
        Assert.Single(partial.Scenarios);
        Assert.Single(partial.ScenarioErrors!);
        Assert.Contains("targetWeights", partial.ScenarioErrors![0]);
        Assert.Equal(1m, partial.Scenarios[0].Weights.Values.Sum() + partial.Scenarios[0].CashWeight);

        var denied = await tools.InvokeAsync(scope with { UserId = Guid.NewGuid() }, call, default);
        Assert.Equal("portfolio_not_found", denied.Error);
        Assert.Null(denied.Data);
    }
}
