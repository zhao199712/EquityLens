using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioRiskMathTests
{
    private static PortfolioRiskMathInputs Inputs()
    {
        var pricesA = Enumerable.Range(0, 40).Select(i => 100m + i).ToList();
        var pricesB = Enumerable.Range(0, 40).Select(i => 80m + i * .5m).ToList();
        static List<decimal> Returns(IReadOnlyList<decimal> values) => Enumerable.Range(1, values.Count - 1).Select(i => (decimal)Math.Log((double)(values[i] / values[i - 1]))).ToList();
        var returnsA = Returns(pricesA); var returnsB = Returns(pricesB);
        var dates = Enumerable.Range(0, 40).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).ToList();
        IReadOnlyList<IReadOnlyList<decimal>> matrix = [returnsA, returnsB];
        var covariance = matrix.Select(x => matrix.Select(y => EquityLens.Api.Domain.Calculations.RiskMath.CalculateCovariance(x, y)).ToArray()).ToArray();
        var assets = new[] { new MathAssetInput("AAA", "USD", 2, 90, dates, pricesA, returnsA), new MathAssetInput("BBB", "USD", 1, 70, dates, pricesB, returnsB) };
        var values = Enumerable.Range(0, 40).Select(i => pricesA[i] * 2 + pricesB[i]).ToList();
        var simple = Enumerable.Range(1, values.Count - 1).Select(i => EquityLens.Api.Domain.Calculations.RiskMath.CalculateReturn(values[i], values[i - 1])).ToList();
        var logs = Enumerable.Range(1, values.Count - 1).Select(i => (decimal)Math.Log((double)(values[i] / values[i - 1]))).ToList();
        return new("Portfolio", Guid.NewGuid(), null, dates[0], dates[^1], assets, [.75m, .25m], matrix, covariance, dates, values, simple, logs, 39, "test", []);
    }

    [Fact]
    public void Registry_ContainsEveryPublicPortfolioAndRiskMathCapability()
    {
        Assert.Equal(37, PortfolioRiskMathCapabilities.All.Count);
        Assert.Equal(37, PortfolioRiskMathCapabilities.All.Select(x => x.Id).Distinct().Count());
        Assert.All(PortfolioRiskMathCapabilities.All, item =>
        {
            Assert.Equal(PortfolioRiskMathNodeTypes.Execute, item.NodeType);
            Assert.NotNull(item.ParametersSchema);
            Assert.Contains(AgentBlackboardKeys.MathInputs, item.RequiredKeys);
        });
        var skill = new WorkflowSkillCatalog().Skills.Single(x => x.Id == "portfolio-risk-mathematics");
        Assert.Equal(38, skill.Capabilities.Count);
    }

    public static IEnumerable<object[]> Operations => PortfolioRiskMathCapabilities.All.Select(x => new object[] { x.Id });

    [Theory]
    [MemberData(nameof(Operations))]
    public void Executor_RunsAllowlistedOperations(string operation)
    {
        var result = new PortfolioRiskMathExecutor().Execute(operation, Inputs(), new JsonObject { ["simulations"] = 20, ["horizonDays"] = 2 }, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Equal(operation, result.Operation);
        Assert.Equal("test", result.Provenance.GetType().GetProperty("Source")?.GetValue(result.Provenance));
    }

    [Fact]
    public void Executor_RejectsPlannerNumericSourceInjection()
    {
        var error = Assert.Throws<AgentNodeException>(() => new PortfolioRiskMathExecutor().Execute("calculate-return", Inputs(), new JsonObject { ["returns"] = new JsonArray(1, 2) }, Guid.NewGuid()));
        Assert.Equal("math_source_injection_forbidden", error.ErrorCode);
    }

    [Fact]
    public void Concentration_ResultSerializesHhiAndLargestWeight()
    {
        var result = new PortfolioRiskMathExecutor().Execute("calculate-concentration", Inputs(), new JsonObject(), Guid.NewGuid());

        var value = Assert.IsType<PortfolioConcentrationMathResult>(result.Value);
        Assert.Equal(.625m, value.Hhi);
        Assert.Equal(.75m, value.LargestWeight);

        var json = JsonNode.Parse(AgentNodeJson.Serialize(result))!;
        Assert.Equal(.625m, json["value"]!["hhi"]!.GetValue<decimal>());
        Assert.Equal(.75m, json["value"]!["largestWeight"]!.GetValue<decimal>());
    }

    [Fact]
    public void Simulation_IsReproducibleForRunAndOperation()
    {
        var executor = new PortfolioRiskMathExecutor(); var runId = Guid.Parse("22222222-2222-2222-2222-222222222222"); var args = new JsonObject { ["simulations"] = 100, ["horizonDays"] = 2 };
        var first = executor.Execute("run-monte-carlo-simulation", Inputs(), args, runId);
        var second = executor.Execute("run-monte-carlo-simulation", Inputs(), args, runId);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Seed, second.Seed);
    }

    [Theory]
    [InlineData("calculate-annualized-volatility")]
    [InlineData("calculate-historical-var")]
    [InlineData("calculate-expected-shortfall")]
    public void PortfolioRiskOperations_UsePortfolioReturnsInsteadOfFirstAsset(string operation)
    {
        var input = Inputs(); var executor = new PortfolioRiskMathExecutor();
        var result = executor.Execute(operation, input, new JsonObject(), Guid.NewGuid());
        var expected = operation switch
        {
            "calculate-annualized-volatility" => EquityLens.Api.Domain.Calculations.RiskMath.CalculateAnnualizedVolatility(EquityLens.Api.Domain.Calculations.RiskMath.CalculateVolatility(input.PortfolioSimpleReturns)),
            "calculate-historical-var" => EquityLens.Api.Domain.Calculations.RiskMath.CalculateHistoricalVaR(input.PortfolioSimpleReturns),
            _ => EquityLens.Api.Domain.Calculations.RiskMath.CalculateExpectedShortfall(input.PortfolioSimpleReturns)
        };
        var firstAsset = operation switch
        {
            "calculate-annualized-volatility" => EquityLens.Api.Domain.Calculations.RiskMath.CalculateAnnualizedVolatility(EquityLens.Api.Domain.Calculations.RiskMath.CalculateVolatility(input.Assets[0].Returns)),
            "calculate-historical-var" => EquityLens.Api.Domain.Calculations.RiskMath.CalculateHistoricalVaR(input.Assets[0].Returns),
            _ => EquityLens.Api.Domain.Calculations.RiskMath.CalculateExpectedShortfall(input.Assets[0].Returns)
        };

        Assert.Equal(expected, Assert.IsType<decimal>(result.Value));
        Assert.NotEqual(firstAsset, Assert.IsType<decimal>(result.Value));
    }

    [Fact]
    public void PortfolioSharpe_UsesAnnualizedPortfolioReturnAndVolatility()
    {
        var input = Inputs(); var result = new PortfolioRiskMathExecutor().Execute("calculate-sharpe-ratio", input, new JsonObject { ["riskFreeRate"] = .02m }, Guid.NewGuid());
        var annualReturn = input.PortfolioSimpleReturns.Average() * 252m;
        var annualVolatility = EquityLens.Api.Domain.Calculations.RiskMath.CalculateAnnualizedVolatility(EquityLens.Api.Domain.Calculations.RiskMath.CalculateVolatility(input.PortfolioSimpleReturns));
        Assert.Equal(EquityLens.Api.Domain.Calculations.RiskMath.CalculateSharpeRatio(annualReturn, .02m, annualVolatility), Assert.IsType<decimal>(result.Value));
    }

    [Fact]
    public async Task InputProvider_AlignsAssetsByCommonTradingDateAndWarnsAboutCurrencies()
    {
        await using var db = CreateDb(); var userId = Guid.NewGuid(); var portfolioId = Guid.NewGuid();
        var first = new Security { Id = Guid.NewGuid(), Ticker = "AAA", Currency = "USD" };
        var second = new Security { Id = Guid.NewGuid(), Ticker = "BBB", Currency = "TWD" };
        var portfolio = new Portfolio { Id = portfolioId, OwnerUserId = userId, Name = "P", Holdings = [new PortfolioHolding { Id = Guid.NewGuid(), PortfolioId = portfolioId, SecurityId = first.Id, Security = first, Quantity = 2, AverageCost = 90 }, new PortfolioHolding { Id = Guid.NewGuid(), PortfolioId = portfolioId, SecurityId = second.Id, Security = second, Quantity = 1, AverageCost = 70 }] };
        db.Portfolios.Add(portfolio);
        AddPrices(db, first.Id, [(1, 100m), (2, 110m), (3, 120m), (4, 130m)]);
        AddPrices(db, second.Id, [(1, 80m), (3, 70m), (4, 75m), (5, 77m)]);
        await db.SaveChangesAsync();
        var board = AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 6));
        var run = new AgentRun { Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis, AgentType = AgentTypes.Portfolio, BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions) };
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = PortfolioRiskMathNodeKeys.PrepareInputs, NodeType = PortfolioRiskMathNodeTypes.PrepareInputs };

        var result = await new PortfolioRiskMathInputProvider().PrepareAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }), CancellationToken.None);

        Assert.Equal([new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3), new DateOnly(2025, 1, 4)], result.CommonTradingDates);
        Assert.Equal([280m, 310m, 335m], result.PortfolioValues);
        Assert.Equal(2, result.PortfolioSimpleReturns.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("multiple currencies", StringComparison.Ordinal));
    }

    private static void AddPrices(EquityLensDbContext db, Guid securityId, IEnumerable<(int Day, decimal Price)> values)
    {
        foreach (var (day, price) in values) db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = securityId, PriceTime = new DateTime(2025, 1, day, 0, 0, 0, DateTimeKind.Utc), Close = price, Interval = "1d", DataSource = "test" });
    }

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); }
    }
}
