using EquityLens.Api.Services.RiskAnalysis;
using System.Text.Json;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class CSharpRiskBacktestEngineTests
{
    [Fact]
    public void Calculate_ValidCanonicalInput_IsReproducible()
    {
        var engine = new CSharpRiskBacktestEngine();
        var input = CreateInput();

        var first = engine.Calculate(input);
        var second = engine.Calculate(input);

        Assert.Equal(RiskEngineOutcomes.Success, first.Outcome);
        Assert.Equal(RiskEngineOutcomes.Success, second.Outcome);
        Assert.NotNull(first.Value);
        Assert.Equal(12, first.Value.ObservationCount);
        Assert.Equal(6, first.Value.Models.Count);
        Assert.Equal(
            JsonSerializer.Serialize(first.Value),
            JsonSerializer.Serialize(second.Value));
    }

    [Fact]
    public void Calculate_MismatchedMatrixDimensions_ReturnsInvalidInput()
    {
        var valid = CreateInput();
        var input = valid with { Weights = [1m] };

        var result = new CSharpRiskBacktestEngine().Calculate(input);

        Assert.Equal(RiskEngineOutcomes.InvalidInput, result.Outcome);
        Assert.Equal("risk.invalid_matrix_dimensions", result.ErrorCode);
    }

    [Fact]
    public void Calculate_InvalidConfidenceLevel_ReturnsInvalidInput()
    {
        var input = CreateInput() with { ConfidenceLevels = [1m] };

        var result = new CSharpRiskBacktestEngine().Calculate(input);

        Assert.Equal(RiskEngineOutcomes.InvalidInput, result.Outcome);
        Assert.Equal("risk.invalid_confidence_level", result.ErrorCode);
    }

    [Fact]
    public void Calculate_SharedCrossLanguageFixture_IsAccepted()
    {
        var fixturePath = Path.Combine(
            FindRepositoryRoot(), "tests", "fixtures", "risk_backtest_input.json");
        var input = JsonSerializer.Deserialize<RiskBacktestEngineInput>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = new CSharpRiskBacktestEngine().Calculate(input!);

        Assert.Equal(RiskEngineOutcomes.Success, result.Outcome);
        Assert.Equal(2, result.Value!.ObservationCount);
    }

    private static RiskBacktestEngineInput CreateInput()
    {
        const int observations = 112;
        var dates = Enumerable.Range(0, observations)
            .Select(index => new DateOnly(2025, 1, 1).AddDays(index))
            .ToArray();
        var first = Enumerable.Range(0, observations)
            .Select(index => 0.001m * (index % 7 - 3))
            .ToArray();
        var second = Enumerable.Range(0, observations)
            .Select(index => 0.0008m * ((index + 2) % 9 - 4))
            .ToArray();
        var portfolio = first.Zip(second, (x, y) => 0.6m * x + 0.4m * y).ToArray();
        return new RiskBacktestEngineInput(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1),
            100,
            100,
            [0.95m, 0.99m],
            0.94m,
            0.05m,
            0.99m,
            dates,
            portfolio,
            [first, second],
            [0.6m, 0.4m]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
