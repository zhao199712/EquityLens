using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Services.PortfolioHoldings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合持倉控制器，提供指定投資組合的持倉查詢與刪除功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
public class PortfolioHoldingsController : ApiControllerBase
{
    private readonly IPortfolioHoldingService _holdingService;

    public PortfolioHoldingsController(IPortfolioHoldingService holdingService)
    {
        _holdingService = holdingService;
    }

    /// <summary>
    /// 取得指定投資組合的所有持倉列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioHoldingResponse>>> GetHoldings(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.ListAsync(portfolioId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 刪除指定投資組合中某支股票的持倉及其所有交易紀錄。
    /// </summary>
    [HttpDelete("by-security/{securityId:guid}")]
    public async Task<IActionResult> DeleteHolding(
        Guid portfolioId,
        Guid securityId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.DeleteBySecurityAsync(portfolioId, securityId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }
}
