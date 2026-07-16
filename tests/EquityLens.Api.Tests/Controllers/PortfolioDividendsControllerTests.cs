using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioDividends;
using EquityLens.Api.Controllers;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.PortfolioDividends;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Tests.Controllers;

public sealed class PortfolioDividendsControllerTests
{
    [Fact]
    public async Task GetLatestDividends_ServiceReturnsList_ReturnsOk()
    {
        var service = new FakePortfolioDividendService(Result<IReadOnlyList<LatestDividendResponse>>.Success(
            new List<LatestDividendResponse>
            {
                new(Guid.NewGuid(), "2330", "台積電", new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 25), 3.5m, "TWD"),
            }));
        var controller = new PortfolioDividendsController(service, new FakeCurrentUserContext());

        var result = await controller.GetLatestDividends(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<LatestDividendResponse>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("2330", list[0].Ticker);
    }

    [Fact]
    public async Task GetLatestDividends_PortfolioNotFound_ReturnsNotFound()
    {
        var service = new FakePortfolioDividendService(
            Result<IReadOnlyList<LatestDividendResponse>>.Failure("portfolio.not_found", "Portfolio was not found."));
        var controller = new PortfolioDividendsController(service, new FakeCurrentUserContext());

        var result = await controller.GetLatestDividends(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(notFound.Value);
        Assert.Equal("portfolio.not_found", error.Code);
    }

    private sealed class FakePortfolioDividendService : IPortfolioDividendService
    {
        private readonly Result<IReadOnlyList<LatestDividendResponse>> _result;

        public FakePortfolioDividendService(Result<IReadOnlyList<LatestDividendResponse>> result)
        {
            _result = result;
        }

        public Task<Result<IReadOnlyList<DividendCashFlowResponse>>> GetCashFlowsAsync(
            Guid portfolioId, Guid userId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result<DividendCashFlowResponse>> UpdateCashFlowAsync(
            Guid portfolioId, Guid cashFlowId, Guid userId, UpdateDividendCashFlowRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result<IReadOnlyList<LatestDividendResponse>>> GetLatestDividendsAsync(
            Guid portfolioId, Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid UserId => Guid.NewGuid();
        public string Email => "test@example.test";
        public string DisplayName => "Test User";
        public bool IsAuthenticated => true;
    }
}
