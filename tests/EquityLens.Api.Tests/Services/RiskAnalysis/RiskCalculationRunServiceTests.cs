using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskCalculationRunServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateAsync_PrimaryDisabled_CompletesWithCSharpFallback()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);

        var result = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal("C# MVEWMA-FHS", result.Value.SelectedModel);
        Assert.Equal(1, result.Value.FallbackDepth);
        Assert.Equal("python_primary_disabled", result.Value.FallbackReason);
        Assert.NotNull(result.Value.Result);
    }

    [Fact]
    public async Task CreateAsync_PrimaryEnabled_EnqueuesAndReturnsRunning()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var queue = new FakeQueue();
        var service = CreateService(db, primaryEnabled: true, queue: queue);

        var result = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Running", result.Value!.Status);
        Assert.Equal(30, result.Value.ProgressPercent);
        Assert.Equal("VT-GARCH-t + Joint-Vector FHS", result.Value.RequestedModel);
        Assert.NotNull(result.Value.InputHash);
        Assert.Single(queue.CalculationEnqueues);
    }

    [Fact]
    public async Task CreateAsync_InvalidOperation_ReturnsFailure()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);

        var result = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("invalid"), UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("risk.invalid_operation", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_PortfolioNotOwned_ReturnsFailure()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);

        var result = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("portfolio.not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_PreservesSimulationsInSnapshot()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);

        var result = await service.CreateAsync(
            PortfolioId, new CreateRiskCalculationRequest("risk", Simulations: 10000), UserId);

        Assert.True(result.IsSuccess);
        var run = await db.RiskCalculationRuns.FirstAsync(x => x.Id == result.Value!.Id);
        var snapshot = JsonDocument.Parse(run.InputSnapshotJson).RootElement;
        Assert.Equal(10000, snapshot.GetProperty("simulations").GetInt32());
    }

    [Fact]
    public async Task GetAsync_UserIsolation_ReturnsNotFoundForWrongUser()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);
        var created = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);

        var result = await service.GetAsync(PortfolioId, created.Value!.Id, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("risk.calculation_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_ReturnsLatestRuns()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);
        await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);
        await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);

        var list = await service.ListAsync(PortfolioId, UserId);

        Assert.Equal(2, list.Count);
        Assert.True(list[0].CreatedAtUtc >= list[1].CreatedAtUtc);
    }

    [Fact]
    public async Task CompletePythonResultAsync_ValidVtGarch_CompletesWithModel()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var queue = new FakeQueue();
        var service = CreateService(db, primaryEnabled: true, queue: queue);
        var created = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);
        var runId = created.Value!.Id;

        var pythonResult = JsonSerializer.Serialize(new
        {
            outcome = "Success",
            value = new
            {
                portfolioId = PortfolioId.ToString(),
                horizons = Array.Empty<object>(),
                fitHealth = new { healthy = true, warningCount = 0, nearUnitRate = 0.1, maxPersistence = 0.98, minNu = 5.0, optimizerAttempts = 2 },
            },
        }, JsonOptions);
        var item = new RiskPythonShadowResultItem(
            "1-1", Guid.NewGuid(), Guid.Empty, runId, "Completed", "key", 100, null);

        await service.CompletePythonResultAsync(item, pythonResult);

        var run = await db.RiskCalculationRuns.FirstAsync(x => x.Id == runId);
        Assert.Equal("Completed", run.Status);
        Assert.Equal("VT-GARCH-t + Joint-Vector FHS", run.SelectedModel);
        Assert.Equal(100, run.ProgressPercent);
        Assert.NotNull(run.FitHealthJson);
        Assert.NotNull(run.ResultJson);
    }

    [Fact]
    public async Task CompletePythonResultAsync_FailedPython_FallsBackToCSharp()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var queue = new FakeQueue();
        var service = CreateService(db, primaryEnabled: true, queue: queue);
        var created = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);
        var runId = created.Value!.Id;

        var item = new RiskPythonShadowResultItem(
            "1-1", Guid.NewGuid(), Guid.Empty, runId, "Failed", null, null, "model_health_failed");

        await service.CompletePythonResultAsync(item, null);

        var run = await db.RiskCalculationRuns.FirstAsync(x => x.Id == runId);
        Assert.Equal("Completed", run.Status);
        Assert.Equal("C# MVEWMA-FHS", run.SelectedModel);
        Assert.Equal(1, run.FallbackDepth);
        Assert.Equal("python_worker_failed", run.FallbackReason);
    }

    [Fact]
    public async Task CompletePythonResultAsync_AlreadyCompleted_IsNoOp()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var service = CreateService(db, primaryEnabled: false);
        var created = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);
        var runId = created.Value!.Id;

        var item = new RiskPythonShadowResultItem(
            "1-1", Guid.NewGuid(), Guid.Empty, runId, "Completed", "key", 100, null);
        await service.CompletePythonResultAsync(item, "{}");

        var run = await db.RiskCalculationRuns.FirstAsync(x => x.Id == runId);
        Assert.Equal("C# MVEWMA-FHS", run.SelectedModel);
    }

    [Fact]
    public async Task CreateAsync_RedisFailure_FallsBackInline()
    {
        await using var db = CreateDb();
        SeedPortfolio(db);
        await db.SaveChangesAsync();
        var queue = new ThrowingQueue();
        var service = CreateService(db, primaryEnabled: true, queue: queue);

        var result = await service.CreateAsync(PortfolioId, new CreateRiskCalculationRequest("risk"), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal("C# MVEWMA-FHS", result.Value.SelectedModel);
        Assert.Equal("python_enqueue_failed", result.Value.FallbackReason);
    }

    private static void SeedPortfolio(EquityLensDbContext db)
    {
        db.Portfolios.Add(new Portfolio
        {
            Id = PortfolioId,
            OwnerUserId = UserId,
            Name = "Test Portfolio",
            BaseCurrency = "TWD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }

    private static RiskCalculationRunService CreateService(
        EquityLensDbContext db,
        bool primaryEnabled,
        IRiskPythonShadowQueue? queue = null)
    {
        var options = Options.Create(new RiskPythonOptions
        {
            PrimaryEnabled = primaryEnabled,
            ShadowEnabled = false,
        });
        return new RiskCalculationRunService(
            db,
            new FakeInputProvider(),
            queue ?? new FakeQueue(),
            new FakeBacktestEngine(),
            new FakeRiskAnalysisService(),
            options);
    }

    private static EquityLensDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private sealed class TestDbContext : EquityLensDbContext
    {
        public TestDbContext(DbContextOptions<EquityLensDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeInputProvider : IRiskBacktestInputProvider
    {
        public Task<Result<RiskBacktestEngineInput>> PreparePortfolioRiskBacktestInputAsync(
            Guid portfolioId, DateOnly from, DateOnly to, Guid providerUserId,
            CancellationToken cancellationToken)
        {
            var dates = Enumerable.Range(0, 260).Select(i => from.AddDays(i)).ToList();
            var returns = Enumerable.Range(0, 260).Select(i => (decimal)(0.001 * Math.Sin(i))).ToList();
            var input = new RiskBacktestEngineInput(
                portfolioId, from, to, 252, 5000,
                [0.95m, 0.99m], 0.94m, 0.05m, 0m,
                dates, returns,
                [returns, returns],
                [0.6m, 0.4m]);
            return Task.FromResult(Result<RiskBacktestEngineInput>.Success(input));
        }
    }

    private sealed class FakeQueue : IRiskPythonShadowQueue
    {
        public readonly List<(Guid RunId, string Operation)> CalculationEnqueues = [];

        public Task<(RiskPythonShadowJob Job, string StreamId)> EnqueueAsync(
            Guid comparisonId, Guid backtestRunId, RiskBacktestEngineInput input, CancellationToken ct)
            => Task.FromResult((new RiskPythonShadowJob(Guid.NewGuid(), comparisonId, backtestRunId, "hash", "key", "v1", DateTime.UtcNow), "1-1"));

        public Task<(Guid JobId, string InputHash, string StreamId)> EnqueueCalculationAsync(
            Guid calculationRunId, string operation, RiskBacktestEngineInput input, CancellationToken ct)
        {
            CalculationEnqueues.Add((calculationRunId, operation));
            return Task.FromResult((Guid.NewGuid(), "abc123hash", "1-1"));
        }

        public Task<RiskPythonShadowResultItem?> ReadResultAsync(string consumerName, CancellationToken ct)
            => Task.FromResult<RiskPythonShadowResultItem?>(null);

        public Task<string?> GetResultJsonAsync(string resultKey, CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task AcknowledgeResultAsync(string streamId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class ThrowingQueue : IRiskPythonShadowQueue
    {
        public Task<(RiskPythonShadowJob Job, string StreamId)> EnqueueAsync(
            Guid comparisonId, Guid backtestRunId, RiskBacktestEngineInput input, CancellationToken ct)
            => throw new InvalidOperationException("Redis unavailable");

        public Task<(Guid JobId, string InputHash, string StreamId)> EnqueueCalculationAsync(
            Guid calculationRunId, string operation, RiskBacktestEngineInput input, CancellationToken ct)
            => throw new InvalidOperationException("Redis unavailable");

        public Task<RiskPythonShadowResultItem?> ReadResultAsync(string consumerName, CancellationToken ct)
            => Task.FromResult<RiskPythonShadowResultItem?>(null);

        public Task<string?> GetResultJsonAsync(string resultKey, CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task AcknowledgeResultAsync(string streamId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeBacktestEngine : IRiskBacktestEngine
    {
        public string EngineName => "fake";
        public string AlgorithmVersion => "fake-v1";
        public RiskBacktestEngineResult Calculate(RiskBacktestEngineInput input, CancellationToken ct)
            => RiskBacktestEngineResult.Failure("InsufficientData", "risk.test", "Fake engine");
    }

    private sealed class FakeRiskAnalysisService : IRiskAnalysisService
    {
        public Task<Result<SecurityRiskResponse>> GetSecurityRiskAsync(
            Guid securityId, DateOnly from, DateOnly to, int horizonDays,
            decimal confidenceLevel, int simulations, CancellationToken ct)
            => Task.FromResult(Result<SecurityRiskResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(
            Guid portfolioId, DateOnly from, DateOnly to, int horizonDays, decimal confidenceLevel,
            int simulations, Guid userId, CancellationToken ct, string modelName = "gbm_ewma_normal",
            IReadOnlyDictionary<Guid, decimal>? targetWeights = null)
        {
            var response = new PortfolioRiskResponse(
                portfolioId, from, to, "TWD", 2, 2, 252,
                100000m, 0.18m, -0.12m, 0.85m, confidenceLevel, simulations,
                "EWMA", 0.94m, "Physical",
                [1, 7, 30],
                [new RiskHorizonResult(1, -0.02m, -0.03m, -0.025m, -0.035m, 100000m, 99000m, 95000m, 105000m)],
                [],
                CovarianceMethod: "MultivariateEWMA",
                ResidualSampling: "FilteredResidual",
                CommonTradingDays: 252,
                ShrinkageAlpha: 0.05m,
                DataAsOfDate: to,
                ConcentrationHhi: 0.25m,
                LargestHoldingWeight: 0.4m,
                RiskSourceAnnualizedVolatility: 0.18m);
            return Task.FromResult(Result<PortfolioRiskResponse>.Success(response));
        }

        public Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(
            Guid portfolioId, DateOnly from, DateOnly to, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioRiskBacktestResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(
            Guid portfolioId, Guid userId, CancellationToken ct, string modelName = "mvewma_fhs")
            => Task.FromResult(Result<PortfolioMonteCarloResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(
            Guid portfolioId, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioRiskGovernanceResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenarioAsync(
            Guid portfolioId, PortfolioRiskScenarioRequest request, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioRiskScenarioResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshotAsync(
            Guid portfolioId, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshotsAsync(
            Guid portfolioId, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshotAsync(
            Guid portfolioId, Guid reportId, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioRiskReportSnapshotDetailResponse>.Failure("risk.test", "Not implemented"));

        public Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(
            Guid portfolioId, Guid userId, CancellationToken ct)
            => Task.FromResult(Result<PortfolioStressTestResponse>.Failure("risk.test", "Not implemented"));
    }
}
