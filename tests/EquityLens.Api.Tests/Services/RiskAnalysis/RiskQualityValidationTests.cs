using System.Text.Json;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Data;
using EquityLens.Api.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskQualityValidationTests
{
    [Fact]
    public async Task EnsureAsync_MissingBacktest_QueuesOnceAndIsIdempotent()
    {
        await using var db = CreateDb();
        var calculation = CompletedCalculation();
        db.RiskCalculationRuns.Add(calculation);
        await db.SaveChangesAsync();
        var queue = new FakeQueue();
        var service = new RiskQualityValidationService(db, queue,
            Options.Create(new RiskPythonOptions { PrimaryEnabled = true }));

        var first = await service.EnsureAsync(calculation.PortfolioId, calculation.Id, calculation.RequestedByUserId);
        var second = await service.EnsureAsync(calculation.PortfolioId, calculation.Id, calculation.RequestedByUserId);

        Assert.True(first.IsSuccess);
        Assert.Equal("Pending", first.Value!.Status);
        Assert.Equal(first.Value.Id, second.Value!.Id);
        Assert.Single(queue.Jobs);
        Assert.Single(db.RiskQualityEvaluations);
    }

    [Fact]
    public async Task EnsureAsync_CompatibleCompletedBacktest_ReusesAndEvaluates()
    {
        await using var db = CreateDb();
        var calculation = CompletedCalculation();
        var backtest = Backtest(150, .20m, .30m);
        backtest.PortfolioId = calculation.PortfolioId;
        backtest.RequestedByUserId = calculation.RequestedByUserId;
        backtest.FromDate = new(2024, 1, 1);
        backtest.ToDate = new(2025, 12, 31);
        backtest.Simulations = 10000;
        backtest.InputHash = calculation.InputHash;
        db.AddRange(calculation, backtest);
        await db.SaveChangesAsync();
        var queue = new FakeQueue();
        var service = new RiskQualityValidationService(db, queue,
            Options.Create(new RiskPythonOptions { PrimaryEnabled = true }));

        var result = await service.EnsureAsync(calculation.PortfolioId, calculation.Id, calculation.RequestedByUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passed", result.Value!.Status);
        Assert.Equal(backtest.Id, result.Value.RiskBacktestRunId);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public void Evaluate_PassingBacktest_MarksPassedAndPreservesEsWarning()
    {
        var evaluation = Evaluation();
        RiskQualityValidationService.Evaluate(evaluation, Calculation(true), Backtest(150, .20m, .30m, "underestimated"));
        Assert.Equal("Passed", evaluation.Status);
        Assert.Contains("ES_UNDERESTIMATED", evaluation.WarningCodesJson);
        Assert.Equal(true, evaluation.FitHealthy);
    }

    [Theory]
    [InlineData(.01, .20, "KUPIEC_FAILED")]
    [InlineData(.20, .01, "CHRISTOFFERSEN_FAILED")]
    public void Evaluate_FailedStatisticalTest_MarksFailed(decimal kupiec, decimal christoffersen, string code)
    {
        var evaluation = Evaluation();
        RiskQualityValidationService.Evaluate(evaluation, Calculation(true), Backtest(150, kupiec, christoffersen));
        Assert.Equal("Failed", evaluation.Status);
        Assert.Contains(code, evaluation.FailureCodesJson);
    }

    [Fact]
    public void Evaluate_UnhealthyFit_MarksFailed()
    {
        var evaluation = Evaluation();
        RiskQualityValidationService.Evaluate(evaluation, Calculation(false), Backtest(150, .20m, .30m));
        Assert.Equal("Failed", evaluation.Status);
        Assert.Contains("FIT_UNHEALTHY", evaluation.FailureCodesJson);
    }

    [Fact]
    public void Evaluate_TooFewObservations_MarksInsufficientData()
    {
        var evaluation = Evaluation();
        RiskQualityValidationService.Evaluate(evaluation, Calculation(true), Backtest(99, null, null));
        Assert.Equal("InsufficientData", evaluation.Status);
        Assert.Contains("INSUFFICIENT_OBSERVATIONS", evaluation.FailureCodesJson);
    }

    private static RiskQualityEvaluation Evaluation() => new()
    {
        Id = Guid.NewGuid(), Model = "VT-GARCH-t + Joint-Vector FHS",
        Status = "Pending", PolicyVersion = RiskQualityPolicy.Version
    };

    private static RiskCalculationRun Calculation(bool healthy) => new()
    {
        Id = Guid.NewGuid(), FitHealthJson = JsonSerializer.Serialize(new { healthy })
    };

    private static RiskCalculationRun CompletedCalculation() => new()
    {
        Id = Guid.NewGuid(), PortfolioId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(),
        Operation = "risk", Status = "Completed", SelectedModel = "VT-GARCH-t + Joint-Vector FHS",
        InputHash = "same-hash", InputSnapshotJson = "{\"from\":\"2024-01-01\",\"to\":\"2025-12-31\",\"simulations\":10000}",
        FitHealthJson = "{\"healthy\":true}", CreatedAtUtc = DateTime.UtcNow
    };

    private static RiskBacktestRun Backtest(int observations, decimal? kupiec, decimal? christoffersen, string esStatus = "aligned")
    {
        var model = new PortfolioRiskBacktestModelResponse(
            "VT-GARCH-t + Joint-Vector FHS", .95m, observations, 5, .05m, .05m,
            kupiec, christoffersen, 5, -.03m, -.03m, 1m, esStatus, "ready", []);
        var result = new PortfolioRiskBacktestResponse(Guid.NewGuid(), new(2024, 1, 1), new(2025, 12, 31), 252, observations, [model]);
        return new RiskBacktestRun { Id = Guid.NewGuid(), Status = "Completed", ResultJson = JsonSerializer.Serialize(result) };
    }

    private static EquityLensDbContext CreateDb() => new TestDbContext(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeQueue : IBackgroundJobQueue
    {
        public List<BackgroundJob> Jobs { get; } = [];
        public Task<string> EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default)
        { Jobs.Add(job); return Task.FromResult("1-0"); }
        public Task<BackgroundJobItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) => Task.FromResult<BackgroundJobItem?>(null);
        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
