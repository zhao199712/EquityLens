using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Services.PortfolioHoldings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合持倉控制器，提供指定投資組合的持倉查詢、建立、更新與刪除功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
public class PortfolioHoldingsController : ApiControllerBase
{
    private readonly IPortfolioHoldingService _holdingService;

    /// <summary>
    /// 初始化投資組合持倉控制器。
    /// </summary>
    /// <param name="holdingService">持倉服務。</param>
    public PortfolioHoldingsController(IPortfolioHoldingService holdingService)
    {
        _holdingService = holdingService;
    }

    /// <summary>
    /// 取得指定投資組合的所有持倉列表。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>持倉列表；若投資組合不存在則返回 404。</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioHoldingResponse>>> GetHoldings(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.ListAsync(portfolioId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 在指定投資組合中建立新的持倉。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="request">建立持倉的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 201 Created 與持倉資料；
    /// 若投資組合不存在則返回 404；
    /// 若證券資料無效則返回 400；
    /// 若持倉已存在則返回 409 Conflict。
    /// </returns>
    [HttpPost]
    public async Task<ActionResult<PortfolioHoldingResponse>> CreateHolding(
        Guid portfolioId,
        CreatePortfolioHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.CreateAsync(portfolioId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode == "holding.duplicate"
                ? Conflict(new ApiError(result.ErrorCode, result.ErrorMessage!))
                : ToActionResult(result);
        }

        return CreatedAtAction(nameof(GetHoldings), new { portfolioId }, result.Value);
    }

    /// <summary>
    /// 更新指定投資組合中的持倉資料。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="holdingId">持倉的唯一識別碼。</param>
    /// <param name="request">更新持倉的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 204 NoContent；
    /// 若投資組合或持倉不存在則返回 404。
    /// </returns>
    [HttpPut("{holdingId:guid}")]
    public async Task<IActionResult> UpdateHolding(
        Guid portfolioId,
        Guid holdingId,
        UpdatePortfolioHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.UpdateAsync(portfolioId, holdingId, request, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

    /// <summary>
    /// 刪除指定投資組合中的持倉。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="holdingId">持倉的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 204 NoContent；
    /// 若投資組合或持倉不存在則返回 404。
    /// </returns>
    [HttpDelete("{holdingId:guid}")]
    public async Task<IActionResult> DeleteHolding(
        Guid portfolioId,
        Guid holdingId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.DeleteAsync(portfolioId, holdingId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }
}
