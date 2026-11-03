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

    [Fact]
    public void ExplicitHoldingDestinationCannotBeReplacedWithCash()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var tools = new PortfolioLoopTools(services.GetRequiredService<IServiceScopeFactory>());
        var request = new PortfolioScenarioRequest("轉入", new Dictionary<string, decimal> { ["2327"] = .3m }, "holding", true, "2330");
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
        if (name == "get_portfolio_snapshot") { Assert.NotNull(result.BlackboardPatch[PortfolioLoopTools.SnapshotKey]); Assert.Single(result.Data["holdings"]!.AsArray()); }
        if (name == "get_risk_report") { Assert.Equal("Hit", result.Data["evidence"]!["cacheStatus"]!.GetValue<string>()); Assert.Equal(.2m, result.Data["metrics"]!["annualizedVolatility"]!.GetValue<decimal>()); }
        if (name == "run_risk_analysis") Assert.Equal(4, result.Data["results"]!.AsArray().Count);
        if (name == "get_performance_attribution") Assert.Equal("complete", result.Data["attribution"]!["coverageStatus"]!.GetValue<string>());
        if (name == "get_risk_profile") Assert.False(result.Data["available"]!.GetValue<bool>());
        Assert.Equal(10, (await db.PortfolioHoldings.SingleAsync()).Quantity);
        Assert.Empty(await db.PortfolioTransactions.ToListAsync());
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
        var snapshot = new PortfolioScenarioSnapshot(id, DateTime.UtcNow, "TWD", dates[0], dates[^1], dates,
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
        Assert.Null(board[PortfolioScenarioCalculator.ResultKey]);
        var denied = await tools.InvokeAsync(scope with { UserId = Guid.NewGuid() }, call, default);
        Assert.Equal("portfolio_not_found", denied.Error);
        Assert.Null(denied.Data);
    }
}
