using System.Text.Json.Nodes;
using EquityLens.Api.Common;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioRiskRunReuseTests
{
    [Fact]
    public async Task ExactInputHashAndCompleteResult_IsFullCacheHit()
    {
        await using var db = CreateDb();
        var input = Input();
        var userId = Guid.NewGuid();
        var run = RiskRun(input, userId, FullResult(input.PortfolioId));
        db.RiskCalculationRuns.Add(run);
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(input)).ResolveAsync(
            Diagnosis(input.PortfolioId), userId, CancellationToken.None);

        Assert.Equal("Hit", resolved.Evidence.CacheStatus);
        Assert.Equal("Unvalidated", resolved.Evidence.QualityStatus);
        Assert.Contains("QUALITY_UNVALIDATED", resolved.Evidence.QualityWarnings!);
        Assert.False(resolved.Evidence.CoreCalculationRequired);
        Assert.Equal(7, resolved.Evidence.ReusedCapabilities.Count);
        Assert.All(resolved.Results, x => Assert.Equal(PortfolioRiskEvidenceSources.PersistedRiskRun, x["source"]!.GetValue<string>()));
    }

    [Fact]
    public async Task PythonRiskResult_IsPartialHitAndReusesAvailableMetrics()
    {
        await using var db = CreateDb();
        var input = Input();
        var userId = Guid.NewGuid();
        db.RiskCalculationRuns.Add(RiskRun(input, userId, PythonResult(input.PortfolioId)));
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(input)).ResolveAsync(
            Diagnosis(input.PortfolioId), userId, CancellationToken.None);

        Assert.Equal("PartialHit", resolved.Evidence.CacheStatus);
        Assert.True(resolved.Evidence.CoreCalculationRequired);
        Assert.Equal([
            "calculate-annualized-volatility", "calculate-max-drawdown",
            "calculate-historical-var", "calculate-expected-shortfall"
        ], resolved.Evidence.ReusedCapabilities);
    }

    [Fact]
    public async Task ChangedInputHash_RejectsPersistedRun()
    {
        await using var db = CreateDb();
        var original = Input();
        var changed = original with { Weights = [.7m, .3m] };
        var userId = Guid.NewGuid();
        db.RiskCalculationRuns.Add(RiskRun(original, userId, FullResult(original.PortfolioId)));
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(changed)).ResolveAsync(
            Diagnosis(original.PortfolioId), userId, CancellationToken.None);

        Assert.Equal("Miss", resolved.Evidence.CacheStatus);
        Assert.Contains(resolved.Evidence.RejectedRuns, x => x.Code == "INPUT_HASH_MISMATCH");
        Assert.Empty(resolved.Results);
    }

    [Fact]
    public async Task DifferentOwner_CannotReuseRiskRun()
    {
        await using var db = CreateDb();
        var input = Input();
        db.RiskCalculationRuns.Add(RiskRun(input, Guid.NewGuid(), FullResult(input.PortfolioId)));
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(input)).ResolveAsync(
            Diagnosis(input.PortfolioId), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Miss", resolved.Evidence.CacheStatus);
        Assert.Empty(resolved.Results);
    }

    [Fact]
    public async Task FailedQualityGate_IsNeverReused()
    {
        await using var db = CreateDb();
        var input = Input();
        var userId = Guid.NewGuid();
        var run = RiskRun(input, userId, FullResult(input.PortfolioId));
        run.QualityEvaluations.Add(Quality(run, "Failed"));
        db.RiskCalculationRuns.Add(run);
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(input)).ResolveAsync(
            Diagnosis(input.PortfolioId), userId, CancellationToken.None);

        Assert.Equal("Miss", resolved.Evidence.CacheStatus);
        Assert.Contains(resolved.Evidence.RejectedRuns, x => x.Code == "QUALITY_GATE_FAILED");
    }

    [Fact]
    public async Task PassedQualityGate_IsPreferredOverNewerLegacyRun()
    {
        await using var db = CreateDb();
        var input = Input();
        var userId = Guid.NewGuid();
        var passed = RiskRun(input, userId, FullResult(input.PortfolioId));
        passed.CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        passed.QualityEvaluations.Add(Quality(passed, "Passed"));
        var legacy = RiskRun(input, userId, FullResult(input.PortfolioId));
        db.AddRange(passed, legacy);
        await db.SaveChangesAsync();

        var resolved = await new PortfolioRiskRunResolver(db, new StubInputProvider(input)).ResolveAsync(
            Diagnosis(input.PortfolioId), userId, CancellationToken.None);

        Assert.Equal("Passed", resolved.Evidence.QualityStatus);
        Assert.Equal(passed.Id, Assert.Single(resolved.Evidence.SourceRiskRunIds));
    }

    private static PortfolioDiagnosisContext Diagnosis(Guid portfolioId) =>
        new(portfolioId, "Test", "TWD", new(2025, 1, 1), new(2025, 12, 31), 2);

    private static RiskBacktestEngineInput Input() => new(
        Guid.NewGuid(), new(2025, 1, 1), new(2025, 12, 31), 750, 10000,
        [.95m, .99m], .94m, .1m, .995m,
        [new(2025, 1, 2), new(2025, 1, 3)], [.01m, -.02m],
        [new decimal[] { .01m, -.01m }, new decimal[] { .005m, -.03m }], [.6m, .4m]);

    private static RiskCalculationRun RiskRun(RiskBacktestEngineInput input, Guid userId, JsonObject result) => new()
    {
        Id = Guid.NewGuid(), PortfolioId = input.PortfolioId, RequestedByUserId = userId,
        Operation = "risk", Status = "Completed", ProgressPercent = 100,
        AlgorithmVersion = "test-v1", InputHash = PortfolioRiskRunResolver.ComputeInputHash(input),
        InputSnapshotJson = "{\"from\":\"2025-01-01\",\"to\":\"2025-12-31\",\"simulations\":10000}",
        ResultJson = result.ToJsonString(), CreatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
    };

    private static RiskQualityEvaluation Quality(RiskCalculationRun run, string status) => new()
    {
        Id = Guid.NewGuid(), RiskCalculationRunId = run.Id, RiskBacktestRunId = Guid.NewGuid(),
        PolicyVersion = RiskQualityPolicy.Version, Status = status, Model = "VT-GARCH-t + Joint-Vector FHS",
        FailureCodesJson = status == "Failed" ? "[\"KUPIEC_FAILED\"]" : "[]", WarningCodesJson = "[]",
        CreatedAtUtc = DateTime.UtcNow
    };

    private static JsonObject FullResult(Guid portfolioId) => new()
    {
        ["portfolioId"] = portfolioId,
        ["dataAsOfDate"] = "2025-12-31",
        ["historicalAnnualizedVolatility"] = .2m,
        ["maxDrawdown"] = -.1m,
        ["concentrationHhi"] = .52m,
        ["largestHoldingWeight"] = .6m,
        ["riskSourceAnnualizedVolatility"] = .18m,
        ["horizons"] = new JsonArray(new JsonObject { ["horizonDays"] = 1, ["historicalVaR"] = -.03m, ["historicalES"] = -.04m }),
        ["holdings"] = new JsonArray(
            new JsonObject { ["componentRiskShare"] = .7m },
            new JsonObject { ["componentRiskShare"] = .3m })
    };

    private static JsonObject PythonResult(Guid portfolioId) => new()
    {
        ["portfolioId"] = portfolioId,
        ["dataAsOfDate"] = "2025-12-31",
        ["historicalAnnualizedVolatility"] = .2m,
        ["maxDrawdown"] = -.1m,
        ["historical"] = new JsonObject { ["var95"] = -.03m, ["es95"] = -.04m }
    };

    private sealed class StubInputProvider(RiskBacktestEngineInput input) : IRiskBacktestInputProvider
    {
        public Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
            Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<RiskBacktestEngineInput>.Success(input));
    }

    private static EquityLensDbContext CreateDb() => new TestDb(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<RiskCalculationRun>().Property(x => x.Id).ValueGeneratedNever();
        }
    }
}
