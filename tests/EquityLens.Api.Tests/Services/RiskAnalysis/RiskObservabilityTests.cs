using System.Diagnostics;
using System.Diagnostics.Metrics;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Observability;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskObservabilityTests
{
    [Fact]
    public async Task PortfolioRisk_EmitsOperationAndCalculationStageSpans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EquityLensTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var service = CreateService();
        var result = await service.GetPortfolioRiskAsync(
            Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2025, 9, 9), 1, .95m, 1000,
            Guid.NewGuid(), default, "mvewma_fhs");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var operation = Assert.Single(activities, activity =>
            activity.OperationName == "risk.portfolio.calculate" && Equals(activity.GetTagItem("risk.simulations"), 1000));
        Assert.Equal("mvewma_fhs", operation.GetTagItem("risk.model"));
        Assert.Equal(1000, operation.GetTagItem("risk.simulations"));
        Assert.Equal("success", operation.GetTagItem("risk.outcome"));
        var operationActivities = activities.Where(activity => activity.TraceId == operation.TraceId).ToList();
        Assert.Contains(operationActivities, activity => activity.OperationName == "risk.portfolio.prices.load");
        Assert.Contains(operationActivities, activity => activity.OperationName == "risk.portfolio.returns.align");
        Assert.Contains(operationActivities, activity => activity.OperationName == "risk.portfolio.risk-sources");
        Assert.Contains(operationActivities, activity => activity.OperationName == "risk.portfolio.horizons");
    }

    [Fact]
    public async Task PortfolioRisk_InvalidModel_EmitsFailureMetricWithoutPortfolioData()
    {
        var measurements = new List<(long Value, string? Operation, string? Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == EquityLensTelemetry.MeterName && instrument.Name == "equitylens.risk.operations")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? operation = null;
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "risk.operation") operation = tag.Value?.ToString();
                if (tag.Key == "risk.outcome") outcome = tag.Value?.ToString();
            }
            measurements.Add((value, operation, outcome));
        });
        listener.Start();

        var result = await CreateService().GetPortfolioRiskAsync(
            Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2025, 9, 9), 1, .95m, 1000,
            Guid.NewGuid(), default, "not-a-model");

        Assert.False(result.IsSuccess);
        Assert.Contains(measurements, measurement =>
            measurement.Value == 1 && measurement.Operation == "risk.portfolio.calculate" && measurement.Outcome == "failure");
    }

    private static RiskAnalysisService CreateService()
    {
        var securityId = Guid.NewGuid();
        var holding = new PortfolioHoldingResponse(
            Guid.NewGuid(), securityId, "SAFE", "TEST", "Test security", 100, 100, "USD", null,
            DateTime.UtcNow, "Technology", "Software");
        var portfolio = new PortfolioDetailResponse(
            Guid.NewGuid(), "Test portfolio", null, "USD", DateTime.UtcNow, DateTime.UtcNow, [holding]);
        var prices = Enumerable.Range(0, 252)
            .Select(index => new MarketPriceResponse(Guid.NewGuid(), securityId, new DateTime(2025, 1, 1).AddDays(index),
                "1d", 100 + index, 101 + index, 99 + index, 100 + index, null, 1000, "Test"))
            .ToList();
        return new RiskAnalysisService(
            new TestSecurityRepository(),
            new TestMarketPriceRepository(securityId, prices),
            new TestPortfolioRepository(portfolio),
            new TestExchangeRateService());
    }

    private sealed class TestSecurityRepository : ISecurityRepository
    {
        public Task<bool> ActiveExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<EquityLens.Api.Contracts.Securities.SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EquityLens.Api.Contracts.Securities.SecurityResponse>>(Array.Empty<EquityLens.Api.Contracts.Securities.SecurityResponse>());
        public Task<EquityLens.Api.Contracts.Securities.SecurityResponse?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<EquityLens.Api.Contracts.Securities.SecurityResponse?>(null);
        public Task<EquityLens.Api.Data.Entities.Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<EquityLens.Api.Data.Entities.Security?>(null);
        public Task<EquityLens.Api.Data.Entities.Security?> GetEntityByTickerExchangeAsync(string ticker, string exchange, CancellationToken cancellationToken) => Task.FromResult<EquityLens.Api.Data.Entities.Security?>(null);
        public Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<EquityLens.Api.Data.Entities.Security>> GetActiveEntitiesAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EquityLens.Api.Data.Entities.Security>>(Array.Empty<EquityLens.Api.Data.Entities.Security>());
        public void Add(EquityLens.Api.Data.Entities.Security security) { }
    }

    private sealed class TestPortfolioRepository(PortfolioDetailResponse portfolio) : IPortfolioRepository
    {
        public Task<IReadOnlyList<PortfolioListItemResponse>> ListActiveAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PortfolioListItemResponse>>(Array.Empty<PortfolioListItemResponse>());
        public Task<PortfolioDetailResponse?> GetDetailAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<PortfolioDetailResponse?>(portfolio);
        public Task<EquityLens.Api.Data.Entities.Portfolio?> GetActiveAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<EquityLens.Api.Data.Entities.Portfolio?>(null);
        public Task<bool> ActiveExistsAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ExistsByNameAsync(Guid ownerUserId, string name, Guid? excludeId, CancellationToken cancellationToken) => Task.FromResult(false);
        public void Add(EquityLens.Api.Data.Entities.Portfolio portfolio) { }
    }

    private sealed class TestMarketPriceRepository(Guid securityId, IReadOnlyList<MarketPriceResponse> prices) : IMarketPriceRepository
    {
        public Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) => Task.FromResult(id == securityId ? prices : (IReadOnlyList<MarketPriceResponse>)Array.Empty<MarketPriceResponse>());
        public Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(IReadOnlyCollection<Guid> securityIds, string interval, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, LatestMarketPrice>>(new Dictionary<Guid, LatestMarketPrice> { [securityId] = new(securityId, 350, DateTime.UtcNow, "Test") });
        public Task<MarketPriceResponse?> GetLatestBySecurityAsync(Guid securityId, string interval, CancellationToken cancellationToken) => Task.FromResult<MarketPriceResponse?>(null);
        public Task<IReadOnlyList<MarketPriceResponse>> GetLatestAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MarketPriceResponse>>(Array.Empty<MarketPriceResponse>());
        public Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(IReadOnlyList<EquityLens.Api.Data.Entities.MarketPrice> prices, CancellationToken cancellationToken) => Task.FromResult(new UpsertMarketPricesResult(0, 0));
    }

    private sealed class TestExchangeRateService : IExchangeRateService
    {
        public Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(amount);
        public Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) => Task.FromResult(1m);
    }
}
