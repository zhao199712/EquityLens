using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioDividends;

namespace EquityLens.Api.Services.PortfolioDividends;

public interface IPortfolioDividendService
{
    Task<Result<IReadOnlyList<LatestDividendResponse>>> GetLatestDividendsAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DividendCashFlowResponse>>> GetCashFlowsAsync(
        Guid portfolioId, Guid userId, CancellationToken cancellationToken = default);

    Task<Result<DividendCashFlowResponse>> UpdateCashFlowAsync(
        Guid portfolioId, Guid cashFlowId, Guid userId, UpdateDividendCashFlowRequest request,
        CancellationToken cancellationToken = default);
}
