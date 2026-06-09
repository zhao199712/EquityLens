using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合估值控制器，提供投資組合的市場估值計算功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/valuation")]
public class PortfolioValuationsController : ApiControllerBase
{
    private readonly IPortfolioValuationService _valuationService;

    /// <summary>
    /// 初始化投資組合估值控制器。
    /// </summary>
    /// <param name="valuationService">投資組合估值服務。</param>
    public PortfolioValuationsController(IPortfolioValuationService valuationService)
    {
        _valuationService = valuationService;
    }

    /// <summary>
    /// 計算指定投資組合的當前市場估值，包含各持倉的市值、損益與權重。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>投資組合估值結果；若投資組合不存在則返回 404。</returns>
    [HttpGet]
    public async Task<ActionResult<PortfolioValuationResponse>> GetValuation(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _valuationService.GetValuationAsync(portfolioId, cancellationToken);
        return ToActionResult(result);
    }
}
