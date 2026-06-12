using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskAnalysisServiceTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly DateOnly To = new(2025, 12, 31);
    private const int HorizonDays = 30;
    private const decimal ConfidenceLevel = 0.95m;
    private const int Simulations = 5000;

    [Fact]
    public async Task GetSecurityRiskAsync_SecurityNotFound_ReturnsFailure()
    {
        var service = CreateService(activeExists: false, prices: null);
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("security.not_found", result.ErrorCode);
    }

    [Fact]
    public async Task GetSecurityRiskAsync_InvalidDateRange_ReturnsFailure()
    {
        var service = CreateService(activeExists: true, prices: null);
        var result = await service.GetSecurityRiskAsync(
            SecurityId, To, From, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("risk.invalid_date_range", result.ErrorCode);
    }

    [Fact]
    public async Task GetSecurityRiskAsync_InsufficientPrices_ReturnsFailure()
    {
        var service = CreateService(activeExists: true, prices: BuildPrices(5));
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("risk.insufficient_prices", result.ErrorCode);
    }

    [Fact]
    public async Task GetSecurityRiskAsync_NonPositivePrice_ReturnsFailure()
    {
        var prices = BuildMutablePrices(100);
        prices[5] = prices[5] with { Close = 0, AdjustedClose = 0 };
        var service = CreateService(activeExists: true, prices: prices);
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("risk.non_positive_price", result.ErrorCode);
    }

    [Fact]
    public async Task GetSecurityRiskAsync_ValidInputs_ReturnsSuccess()
    {
        var service = CreateService(activeExists: true, prices: BuildPrices(252));
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.True(result.IsSuccess, $"Expected success but got error: {result.ErrorCode}:{result.ErrorMessage}");

        var response = result.Value!;
        Assert.Equal(SecurityId, response.SecurityId);
        Assert.Equal(From, response.From);
        Assert.Equal(To, response.To);
        Assert.Equal(252, response.PriceCount);
        Assert.Equal(ConfidenceLevel, response.ConfidenceLevel);
        Assert.Equal(Simulations, response.Simulations);
        Assert.Equal("EWMA", response.VolatilityMethod);
        Assert.Equal(0.94m, response.EwmaLambda);
        Assert.Equal("ZeroDrift", response.DriftAssumption);
        Assert.Equal(new[] { 1, 7, 30 }, response.SupportedHorizons);
        Assert.Equal(3, response.Horizons.Count);
        Assert.Equal(new[] { 1, 7, 30 }, response.Horizons.Select(x => x.HorizonDays));
        Assert.Equal(0, response.AnnualizedDrift);
        Assert.True(response.AnnualizedVolatility > 0);
        foreach (var horizon in response.Horizons)
        {
            Assert.True(horizon.HistoricalVaR < 0);
            Assert.True(horizon.HistoricalES <= horizon.HistoricalVaR);
            Assert.True(horizon.MonteCarloVaR < 0);
            Assert.True(horizon.MonteCarloES <= horizon.MonteCarloVaR);
            Assert.True(horizon.MonteCarloMeanFinalValue > 0);
            Assert.True(horizon.MonteCarloBestCaseFinalValue >= horizon.MonteCarloWorstCaseFinalValue);
        }
    }

    [Fact]
    public async Task GetSecurityRiskAsync_DifferentConfidenceLevel_ReflectedInResponse()
    {
        var service = CreateService(activeExists: true, prices: BuildPrices(252));
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, 0.99m, Simulations, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(0.99m, result.Value!.ConfidenceLevel);
    }

    [Fact]
    public async Task GetSecurityRiskAsync_AdjustedClosePreferredOverClose()
    {
        var prices = BuildMutablePrices(100);
        prices[0] = prices[0] with { Close = 100, AdjustedClose = 95 };
        var service = CreateService(activeExists: true, prices: prices);
        var result = await service.GetSecurityRiskAsync(
            SecurityId, From, To, HorizonDays, ConfidenceLevel, Simulations, default);
        Assert.True(result.IsSuccess);
    }

    // ── Portfolio Risk Tests ──

    [Fact]
    public async Task GetPortfolioRiskAsync_PortfolioNotFound_ReturnsFailure()
    {
        var service = CreatePortfolioService(
            portfolio: null,
            latestPrices: null,
            historicalPricesBySecurity: null);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, From, To, HorizonDays, 0.95m, 5000, UserId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("portfolio.not_found", result.ErrorCode);
    }

    [Fact]
    public async Task GetPortfolioRiskAsync_NoHoldings_ReturnsFailure()
    {
        var emptyPortfolio = new PortfolioDetailResponse(
            PortfolioId, "Test", null, "TWD", DateTime.UtcNow, DateTime.UtcNow,
            Array.Empty<PortfolioHoldingResponse>());
        var service = CreatePortfolioService(
            portfolio: emptyPortfolio,
            latestPrices: null,
            historicalPricesBySecurity: null);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, From, To, HorizonDays, 0.95m, 5000, UserId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("portfolio.no_holdings", result.ErrorCode);
    }

    [Fact]
    public async Task GetPortfolioRiskAsync_ValidInputs_ReturnsSuccess()
    {
        var secId1 = Guid.NewGuid();
        var secId2 = Guid.NewGuid();

        var holdings = new List<PortfolioHoldingResponse>
        {
            new(Guid.NewGuid(), secId1, "AAPL", "NASDAQ", "Apple", 100, 150, "USD", null, DateTime.UtcNow),
            new(Guid.NewGuid(), secId2, "MSFT", "NASDAQ", "Microsoft", 50, 300, "USD", null, DateTime.UtcNow),
        };

        var portfolio = new PortfolioDetailResponse(
            PortfolioId, "Test Portfolio", null, "USD",
            DateTime.UtcNow, DateTime.UtcNow, holdings);

        var latestPrices = new Dictionary<Guid, LatestMarketPrice>
        {
            [secId1] = new(secId1, 200, new DateTime(2025, 12, 31), "Test"),
            [secId2] = new(secId2, 400, new DateTime(2025, 12, 31), "Test"),
        };

        var baseDate = new DateTime(2025, 1, 1);
        var prices1 = new List<MarketPriceResponse>(50);
        var prices2 = new List<MarketPriceResponse>(50);
        var price = 100m;
        for (var i = 0; i < 50; i++)
        {
            price += (decimal)(new Random(i).NextDouble() - 0.5) * 5;
            if (price <= 0) price = 50;
            prices1.Add(new(Guid.NewGuid(), secId1, baseDate.AddDays(i),
                "1d", price, price + 1, price - 1, price, null, 1000 + i, "Test"));
            prices2.Add(new(Guid.NewGuid(), secId2, baseDate.AddDays(i),
                "1d", price + 10, price + 11, price + 9, price + 10, null, 1000 + i, "Test"));
        }

        var historicalPrices = new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [secId1] = prices1,
            [secId2] = prices2,
        };

        var service = CreatePortfolioService(portfolio, latestPrices, historicalPrices);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, DateOnly.FromDateTime(baseDate), DateOnly.FromDateTime(baseDate.AddDays(49)),
            HorizonDays, 0.95m, 5000, UserId, default);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.ErrorCode}:{result.ErrorMessage}");
        var response = result.Value!;
        Assert.Equal(PortfolioId, response.PortfolioId);
        Assert.Equal("USD", response.BaseCurrency);
        Assert.Equal(2, response.HoldingCount);
        Assert.Equal(2, response.PricedHoldingCount);
        Assert.True(response.AlignedReturnCount > 0);
        Assert.True(response.TotalMarketValue > 0);
        Assert.True(response.HistoricalAnnualizedVolatility >= 0);
        Assert.Equal("EWMA", response.VolatilityMethod);
        Assert.Equal(0.94m, response.EwmaLambda);
        Assert.Equal("ZeroDrift", response.DriftAssumption);
        Assert.Equal(new[] { 1, 7, 30 }, response.SupportedHorizons);
        Assert.Equal(3, response.Horizons.Count);
        foreach (var horizon in response.Horizons)
        {
            Assert.True(horizon.HistoricalVaR <= 0);
            Assert.True(horizon.HistoricalES <= horizon.HistoricalVaR);
            Assert.True(horizon.MonteCarloMeanFinalValue > response.TotalMarketValue * 0.5m);
        }
        Assert.Equal(0.95m, response.ConfidenceLevel);
        Assert.Equal(2, response.Holdings.Count);
    }

    [Fact]
    public async Task GetPortfolioRiskAsync_SinglePricedHolding_ReturnsMonteCarloValues()
    {
        var secId = Guid.NewGuid();
        var holdings = new List<PortfolioHoldingResponse>
        {
            new(Guid.NewGuid(), secId, "AAPL", "NASDAQ", "Apple", 100, 150, "USD", null, DateTime.UtcNow),
        };

        var portfolio = new PortfolioDetailResponse(
            PortfolioId, "Test Portfolio", null, "USD",
            DateTime.UtcNow, DateTime.UtcNow, holdings);

        var latestPrices = new Dictionary<Guid, LatestMarketPrice>
        {
            [secId] = new(secId, 200, new DateTime(2025, 12, 31), "Test"),
        };

        var historicalPrices = new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [secId] = BuildPricesForSecurity(secId, 50),
        };

        var service = CreatePortfolioService(portfolio, latestPrices, historicalPrices);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, From, To, HorizonDays, 0.95m, 5000, UserId, default);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.ErrorCode}:{result.ErrorMessage}");
        Assert.All(result.Value!.Horizons,
            horizon => Assert.True(horizon.MonteCarloMeanFinalValue > result.Value.TotalMarketValue * 0.5m));
    }

    [Fact]
    public async Task GetPortfolioRiskAsync_NonPositivePrice_ReturnsFailure()
    {
        var secId = Guid.NewGuid();
        var holdings = new List<PortfolioHoldingResponse>
        {
            new(Guid.NewGuid(), secId, "AAPL", "NASDAQ", "Apple", 100, 150, "USD", null, DateTime.UtcNow),
        };

        var portfolio = new PortfolioDetailResponse(
            PortfolioId, "Test Portfolio", null, "USD",
            DateTime.UtcNow, DateTime.UtcNow, holdings);

        var latestPrices = new Dictionary<Guid, LatestMarketPrice>
        {
            [secId] = new(secId, 200, new DateTime(2025, 12, 31), "Test"),
        };

        var prices = BuildPricesForSecurity(secId, 50).ToList();
        prices[10] = prices[10] with { Close = 0 };
        var historicalPrices = new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [secId] = prices,
        };

        var service = CreatePortfolioService(portfolio, latestPrices, historicalPrices);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, From, To, HorizonDays, 0.95m, 5000, UserId, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("risk.non_positive_price", result.ErrorCode);
    }

    [Fact]
    public async Task GetPortfolioRiskAsync_InvalidDateRange_ReturnsFailure()
    {
        var holdings = new List<PortfolioHoldingResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "AAPL", "NASDAQ", "Apple", 100, 150, "USD", null, DateTime.UtcNow),
        };
        var portfolio = new PortfolioDetailResponse(
            PortfolioId, "Test", null, "USD",
            DateTime.UtcNow, DateTime.UtcNow, holdings);
        var latest = new Dictionary<Guid, LatestMarketPrice>
        {
            [holdings[0].SecurityId] = new(holdings[0].SecurityId, 100, DateTime.UtcNow, "Test"),
        };
        var service = CreatePortfolioService(portfolio, latest, null);
        var result = await service.GetPortfolioRiskAsync(
            PortfolioId, To, From, HorizonDays, 0.95m, 5000, UserId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("risk.invalid_date_range", result.ErrorCode);
    }

    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static RiskAnalysisService CreatePortfolioService(
        PortfolioDetailResponse? portfolio,
        IReadOnlyDictionary<Guid, LatestMarketPrice>? latestPrices,
        IReadOnlyDictionary<Guid, IReadOnlyList<MarketPriceResponse>>? historicalPricesBySecurity)
    {
        return new RiskAnalysisService(
            new TestSecurityRepository(true),
            new TestMultiMarketPriceRepository(latestPrices, historicalPricesBySecurity),
            new TestPortfolioRepository(portfolio),
            new TestExchangeRateService());
    }

    private static RiskAnalysisService CreateService(
        bool activeExists, IReadOnlyList<MarketPriceResponse>? prices)
    {
        return new RiskAnalysisService(
            new TestSecurityRepository(activeExists),
            new TestMarketPriceRepository(prices ?? Array.Empty<MarketPriceResponse>()),
            new TestPortfolioRepository(),
            new TestExchangeRateService());
    }

    private sealed class TestPortfolioRepository : IPortfolioRepository
    {
        private readonly PortfolioDetailResponse? _portfolio;

        public TestPortfolioRepository(PortfolioDetailResponse? portfolio = null)
        {
            _portfolio = portfolio;
        }

        public Task<IReadOnlyList<PortfolioListItemResponse>> ListActiveAsync(
            Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PortfolioListItemResponse>>(Array.Empty<PortfolioListItemResponse>());

        public Task<PortfolioDetailResponse?> GetDetailAsync(
            Guid id, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult(_portfolio);

        public Task<Portfolio?> GetActiveAsync(Guid id, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult<Portfolio?>(null);

        public Task<bool> ActiveExistsAsync(Guid id, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ExistsByNameAsync(
            Guid ownerUserId, string name, Guid? excludeId, CancellationToken ct) =>
            Task.FromResult(false);

        public void Add(Portfolio portfolio) { }
    }

    private sealed class TestExchangeRateService : IExchangeRateService
    {
        public Task<decimal> ConvertAsync(
            decimal amount, string fromCurrency, string toCurrency, CancellationToken ct) =>
            Task.FromResult(amount);

        public Task<decimal> GetRateAsync(
            string fromCurrency, string toCurrency, CancellationToken ct) =>
            Task.FromResult(1m);
    }

    private static MarketPriceResponse[] BuildPrices(int count)
    {
        var prices = new MarketPriceResponse[count];
        var baseDate = new DateTime(2024, 1, 1);
        var basePrice = 100m;
        for (var i = 0; i < count; i++)
        {
            basePrice += (decimal)(new Random(i).NextDouble() - 0.5) * 2;
            if (basePrice <= 0) basePrice = 10;
            prices[i] = new MarketPriceResponse(
                Guid.NewGuid(),
                SecurityId,
                baseDate.AddDays(i),
                "1d",
                basePrice,
                basePrice + 1,
                basePrice - 1,
                basePrice,
                null,
                1000 + i,
                "Test");
        }
        return prices;
    }

    private static MarketPriceResponse[] BuildPricesForSecurity(Guid securityId, int count)
    {
        var prices = new MarketPriceResponse[count];
        var baseDate = new DateTime(2024, 1, 1);
        var basePrice = 100m;
        for (var i = 0; i < count; i++)
        {
            basePrice += (decimal)(new Random(i).NextDouble() - 0.5) * 2;
            if (basePrice <= 0) basePrice = 10;
            prices[i] = new MarketPriceResponse(
                Guid.NewGuid(),
                securityId,
                baseDate.AddDays(i),
                "1d",
                basePrice,
                basePrice + 1,
                basePrice - 1,
                basePrice,
                null,
                1000 + i,
                "Test");
        }
        return prices;
    }

    private static List<MarketPriceResponse> BuildMutablePrices(int count)
    {
        return BuildPrices(count).ToList();
    }

    private sealed class TestSecurityRepository : ISecurityRepository
    {
        private readonly bool _activeExists;

        public TestSecurityRepository(bool activeExists) => _activeExists = activeExists;

        public Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_activeExists);

        public Task<IReadOnlyList<Contracts.Securities.SecurityResponse>> SearchAsync(
            string? query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Contracts.Securities.SecurityResponse>>(Array.Empty<Contracts.Securities.SecurityResponse>());

        public Task<Contracts.Securities.SecurityResponse?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Contracts.Securities.SecurityResponse?>(null);

        public Task<Data.Entities.Security?> GetEntityAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Data.Entities.Security?>(null);

        public Task<Data.Entities.Security?> GetEntityByTickerExchangeAsync(
            string ticker, string exchange, CancellationToken ct) =>
            Task.FromResult<Data.Entities.Security?>(null);

        public Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<Data.Entities.Security>> GetActiveEntitiesAsync(
            int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Data.Entities.Security>>(Array.Empty<Data.Entities.Security>());

        public void Add(Data.Entities.Security security) { }
    }

    private sealed class TestMarketPriceRepository : IMarketPriceRepository
    {
        private readonly IReadOnlyList<MarketPriceResponse> _prices;

        public TestMarketPriceRepository(IReadOnlyList<MarketPriceResponse> prices) => _prices = prices;

        public Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(
            IReadOnlyCollection<Guid> securityIds, string interval, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, LatestMarketPrice>>(new Dictionary<Guid, LatestMarketPrice>());

        public Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(
            Guid securityId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            Task.FromResult(_prices);

        public Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(
            IReadOnlyList<Data.Entities.MarketPrice> prices, CancellationToken ct) =>
            Task.FromResult(new UpsertMarketPricesResult(0, 0));
    }

    private sealed class TestMultiMarketPriceRepository : IMarketPriceRepository
    {
        private readonly IReadOnlyDictionary<Guid, LatestMarketPrice>? _latestPrices;
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<MarketPriceResponse>>? _historicalPrices;

        public TestMultiMarketPriceRepository(
            IReadOnlyDictionary<Guid, LatestMarketPrice>? latestPrices,
            IReadOnlyDictionary<Guid, IReadOnlyList<MarketPriceResponse>>? historicalPrices)
        {
            _latestPrices = latestPrices;
            _historicalPrices = historicalPrices;
        }

        public Task<IReadOnlyDictionary<Guid, LatestMarketPrice>> GetLatestPricesAsync(
            IReadOnlyCollection<Guid> securityIds, string interval, CancellationToken ct)
        {
            if (_latestPrices is null)
                return Task.FromResult<IReadOnlyDictionary<Guid, LatestMarketPrice>>(
                    new Dictionary<Guid, LatestMarketPrice>());
            return Task.FromResult(_latestPrices);
        }

        public Task<IReadOnlyList<MarketPriceResponse>> GetBySecurityAsync(
            Guid securityId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (_historicalPrices is null || !_historicalPrices.TryGetValue(securityId, out var prices))
                return Task.FromResult<IReadOnlyList<MarketPriceResponse>>(Array.Empty<MarketPriceResponse>());
            return Task.FromResult(prices);
        }

        public Task<UpsertMarketPricesResult> UpsertDailyPricesAsync(
            IReadOnlyList<Data.Entities.MarketPrice> prices, CancellationToken ct) =>
            Task.FromResult(new UpsertMarketPricesResult(0, 0));
    }
}
