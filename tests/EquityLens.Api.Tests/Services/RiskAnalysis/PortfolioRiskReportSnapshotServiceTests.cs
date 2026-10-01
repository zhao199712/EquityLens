using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.RiskAnalysis;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class PortfolioRiskReportSnapshotServiceTests
{
    [Fact]
    public async Task CompleteReport_PreservesSnapshotContractAndRoundTripsThroughReadEndpoints()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedPortfolioAsync(db);
        var operations = new ReportOperations();
        var service = new PortfolioRiskReportSnapshotService(new PortfolioRepository(db),
            operations, operations, operations, operations, operations, db);

        var created = await service.CreatePortfolioRiskReportSnapshotAsync(portfolio.Id, portfolio.OwnerUserId, default);

        Assert.True(created.IsSuccess);
        Assert.Equal(new[] { "risk", "governance", "stress", "monteCarlo", "backtest" }, operations.Calls);
        var report = created.Value!;
        Assert.Equal("MVEWMA-FHS", report.Model);
        Assert.Equal("balanced-v1", report.ThresholdVersion);
        Assert.Equal("ready", report.OverallStatus);
        Assert.Equal("ready", report.Snapshot.GetProperty("dataQuality").GetProperty("status").GetString());
        Assert.Equal(.95m, report.Snapshot.GetProperty("risk").GetProperty("confidenceLevel").GetDecimal());
        Assert.Equal(new[] { "dataQuality", "governance", "concentration", "risk", "holdings", "industries", "backtest", "stress", "monteCarlo" },
            report.Snapshot.EnumerateObject().Select(x => x.Name));
        Assert.Single(await db.RiskReportSnapshots.ToListAsync());

        var listed = await service.GetPortfolioRiskReportSnapshotsAsync(portfolio.Id, portfolio.OwnerUserId, default);
        var read = await service.GetPortfolioRiskReportSnapshotAsync(portfolio.Id, report.Id, portfolio.OwnerUserId, default);
        Assert.Equal(report.Id, Assert.Single(listed.Value!).Id);
        Assert.Equal(report.Snapshot.GetRawText(), read.Value!.Snapshot.GetRawText());
        Assert.Equal(5, operations.Calls.Count);
    }

    [Theory]
    [InlineData("riskFailure")]
    [InlineData("monteCarloUnavailable")]
    [InlineData("backtestUnavailable")]
    [InlineData("stressUnavailable")]
    public async Task IncompleteReport_DoesNotPersistSnapshotOrShortCircuitExistingOperationOrder(string unavailable)
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedPortfolioAsync(db);
        var operations = new ReportOperations(unavailable);
        var service = new PortfolioRiskReportSnapshotService(new PortfolioRepository(db),
            operations, operations, operations, operations, operations, db);

        var result = await service.CreatePortfolioRiskReportSnapshotAsync(portfolio.Id, portfolio.OwnerUserId, default);

        Assert.Equal("risk.snapshot_unavailable", result.ErrorCode);
        Assert.Equal(new[] { "risk", "governance", "stress", "monteCarlo", "backtest" }, operations.Calls);
        Assert.Empty(await db.RiskReportSnapshots.ToListAsync());
    }

    [Fact]
    public async Task SnapshotOperations_RejectAnotherUserBeforeReadingOrCalculating()
    {
        await using var db = RefactorTestDbContext.Create();
        var portfolio = await SeedPortfolioAsync(db);
        var report = new RiskReportSnapshot
        {
            Id = Guid.NewGuid(), PortfolioId = portfolio.Id, CreatedByUserId = portfolio.OwnerUserId,
            Model = "MVEWMA-FHS", ThresholdVersion = "balanced-v1", SnapshotJson = """{"dataQuality":{"status":"ready"}}"""
        };
        db.RiskReportSnapshots.Add(report);
        await db.SaveChangesAsync();
        var operations = new ReportOperations();
        var service = new PortfolioRiskReportSnapshotService(new PortfolioRepository(db),
            operations, operations, operations, operations, operations, db);
        var otherUser = Guid.NewGuid();

        Assert.Equal("portfolio.not_found", (await service.CreatePortfolioRiskReportSnapshotAsync(portfolio.Id, otherUser, default)).ErrorCode);
        Assert.Equal("portfolio.not_found", (await service.GetPortfolioRiskReportSnapshotsAsync(portfolio.Id, otherUser, default)).ErrorCode);
        Assert.Equal("portfolio.not_found", (await service.GetPortfolioRiskReportSnapshotAsync(portfolio.Id, report.Id, otherUser, default)).ErrorCode);
        Assert.Empty(operations.Calls);
        Assert.Single(await db.RiskReportSnapshots.ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task<Portfolio> SeedPortfolioAsync(RefactorTestDbContext db)
    {
        var portfolio = new Portfolio { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Fixture" };
        db.Portfolios.Add(portfolio);
        await db.SaveChangesAsync();
        return portfolio;
    }

    private sealed class ReportOperations(string? unavailable = null) :
        IPortfolioRiskCalculator, IPortfolioRiskGovernanceService, IPortfolioStressTestService,
        IPortfolioMonteCarloService, IPortfolioRiskBacktestService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public List<string> Calls { get; } = [];

        public Task<Result<PortfolioRiskResponse>> GetPortfolioRiskAsync(Guid portfolioId, DateOnly from, DateOnly to,
            int horizonDays, decimal confidenceLevel, int simulations, Guid providerUserId, CancellationToken cancellationToken,
            string modelName = "gbm_ewma_normal", IReadOnlyDictionary<Guid, decimal>? targetWeights = null)
        {
            Calls.Add("risk");
            if (unavailable == "riskFailure")
                return Task.FromResult(Result<PortfolioRiskResponse>.Failure("risk.insufficient_prices", "fixture"));
            var risk = Deserialize<PortfolioRiskResponse>("""{"baseCurrency":"TWD","confidenceLevel":0.95,"supportedHorizons":[1,7,30],"horizons":[],"holdings":[],"industries":[],"dataAsOfDate":"2026-10-02","commonTradingDays":200}""");
            return Task.FromResult(Result<PortfolioRiskResponse>.Success(risk with { PortfolioId = portfolioId, From = from, To = to }));
        }

        public Task<Result<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernanceAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
        {
            Calls.Add("governance");
            return Task.FromResult(Result<PortfolioRiskGovernanceResponse>.Success(new(portfolioId, "ready", new DateOnly(2026, 10, 2), 200, [])));
        }

        public Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestAsync(Guid portfolioId, Guid providerUserId, CancellationToken cancellationToken)
        {
            Calls.Add("stress");
            IReadOnlyList<PortfolioStressScenarioResponse> scenarios =
            [new("fixture", "Fixture", "historical", unavailable == "stressUnavailable" ? "insufficient_prices" : "ready", "fixture", null, null, 0, [], [])];
            return Task.FromResult(Result<PortfolioStressTestResponse>.Success(new(portfolioId, new DateOnly(2026, 10, 2), scenarios)));
        }

        public Task<Result<PortfolioStressTestResponse>> GetPortfolioStressTestWithWeightsAsync(Guid portfolioId, Guid providerUserId,
            IReadOnlyDictionary<Guid, decimal> targetWeights, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<Result<PortfolioMonteCarloResponse>> GetPortfolioMonteCarloAsync(Guid portfolioId, Guid providerUserId,
            CancellationToken cancellationToken, string modelName = "mvewma_fhs")
        {
            Calls.Add("monteCarlo");
            var response = Deserialize<PortfolioMonteCarloResponse>("""{"status":"ready","bands":[],"samplePaths":[],"diagnostics":{}}""");
            return Task.FromResult(Result<PortfolioMonteCarloResponse>.Success(response with
            {
                PortfolioId = portfolioId, Status = unavailable == "monteCarloUnavailable" ? "insufficient_prices" : "ready"
            }));
        }

        public Task<Result<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktestAsync(Guid portfolioId, DateOnly from,
            DateOnly to, Guid providerUserId, CancellationToken cancellationToken)
        {
            Calls.Add("backtest");
            var model = Deserialize<PortfolioRiskBacktestModelResponse>("""{"model":"fixture","status":"ready","points":[]}""");
            return Task.FromResult(Result<PortfolioRiskBacktestResponse>.Success(new(portfolioId, from, to, 252, 100,
                [model with { Status = unavailable == "backtestUnavailable" ? "insufficient_prices" : "ready" }])));
        }

        private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    }
}
