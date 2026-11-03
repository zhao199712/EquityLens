using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
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
