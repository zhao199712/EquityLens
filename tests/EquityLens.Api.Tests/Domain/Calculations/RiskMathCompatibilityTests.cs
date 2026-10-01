using System.Text.Json;
using EquityLens.Api.Domain.Calculations;

namespace EquityLens.Api.Tests.Domain.Calculations;

public sealed class RiskMathCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The fixture was produced with the pre-refactor assembly, using these inputs and seed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeededFhsDistribution_MatchesPreRefactorResult(bool conservative)
    {
        using var fixture = ReadFixture();
        var expected = fixture.RootElement.GetProperty(conservative ? "conservative" : "base")
            .Deserialize<MvewmaFhsResult[]>(JsonOptions)!;
        var actual = RiskMath.RunMultivariateFhsSimulationForConfidenceLevels(
            ReturnMatrix(), [.6m, .4m], 100000m, 7, 100, [.95m, .99m],
            residualCapQuantile: conservative ? .99m : 0m, randomSeed: 456);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeededFhsPaths_MatchPreRefactorBandsDiagnosticsAndSamplePaths(bool conservative)
    {
        using var fixture = ReadFixture();
        var expected = fixture.RootElement.GetProperty(conservative ? "conservativePath" : "path")
            .Deserialize<MvewmaFhsPathResult>(JsonOptions)!;
        var actual = RiskMath.RunMultivariateFhsPathSimulation(
            ReturnMatrix(), [.6m, .4m], 7, 100, 2, 456,
            residualCapQuantile: conservative ? .99m : 0m);

        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    }

    private static IReadOnlyList<IReadOnlyList<decimal>> ReturnMatrix() =>
    [
        Enumerable.Range(0, 40).Select(i => (i % 7 - 3) * .003m + (i % 3 - 1) * .001m).ToArray(),
        Enumerable.Range(0, 40).Select(i => (i % 5 - 2) * .002m - (i % 7 - 3) * .001m).ToArray()
    ];

    private static JsonDocument ReadFixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RiskMath", "seeded-fhs.json")));
}
