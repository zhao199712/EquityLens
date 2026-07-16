using EquityLens.Api.Contracts.PortfolioDividends;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.PortfolioDividends;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合股息控制器，提供每支持倉最近一筆股息資訊。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/dividends")]
public class PortfolioDividendsController : ApiControllerBase
{
    private readonly IPortfolioDividendService _dividendService;
    private readonly ICurrentUserContext _currentUser;

    public PortfolioDividendsController(
        IPortfolioDividendService dividendService,
        ICurrentUserContext currentUser)
    {
        _dividendService = dividendService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 取得指定投資組合每支持倉最近一筆股息。
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<IReadOnlyList<LatestDividendResponse>>> GetLatestDividends(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _dividendService.GetLatestDividendsAsync(
            portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("cash-flows")]
    public async Task<ActionResult<IReadOnlyList<DividendCashFlowResponse>>> GetCashFlows(
        Guid portfolioId, CancellationToken cancellationToken)
    {
        var result = await _dividendService.GetCashFlowsAsync(portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("cash-flows/{cashFlowId:guid}")]
    public async Task<ActionResult<DividendCashFlowResponse>> UpdateCashFlow(
        Guid portfolioId, Guid cashFlowId, UpdateDividendCashFlowRequest request, CancellationToken cancellationToken)
    {
        var result = await _dividendService.UpdateCashFlowAsync(
            portfolioId, cashFlowId, _currentUser.UserId, request, cancellationToken);
        return ToActionResult(result);
    }
}
