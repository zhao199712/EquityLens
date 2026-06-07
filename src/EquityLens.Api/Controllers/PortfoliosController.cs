using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.Portfolios;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合控制器，提供投資組合的查詢、建立、更新與刪除功能。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PortfoliosController : ApiControllerBase
{
    private readonly IPortfolioService _portfolioService;

    /// <summary>
    /// 初始化投資組合控制器。
    /// </summary>
    /// <param name="portfolioService">投資組合服務。</param>
    public PortfoliosController(IPortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    /// <summary>
    /// 取得當前使用者的所有活躍投資組合列表。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>投資組合摘要列表。</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioListItemResponse>>> GetPortfolios(CancellationToken cancellationToken)
    {
        var portfolios = await _portfolioService.ListAsync(cancellationToken);
        return Ok(portfolios);
    }

    /// <summary>
    /// 依據投資組合識別碼取得詳細資料。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>投資組合詳細資料；若不存在則返回 404。</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioDetailResponse>> GetPortfolio(Guid id, CancellationToken cancellationToken)
    {
        var result = await _portfolioService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 建立新的投資組合。
    /// </summary>
    /// <param name="request">建立投資組合的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 201 Created 與投資組合詳細資料；
    /// 若名稱為空白則返回 400。
    /// </returns>
    [HttpPost]
    public async Task<ActionResult<PortfolioDetailResponse>> CreatePortfolio(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _portfolioService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(GetPortfolio), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// 更新指定投資組合的資料。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="request">更新投資組合的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 204 NoContent；
    /// 若名稱為空白則返回 400；
    /// 若投資組合不存在則返回 404。
    /// </returns>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePortfolio(
        Guid id,
        UpdatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _portfolioService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

    /// <summary>
    /// 軟刪除指定投資組合。
    /// </summary>
    /// <param name="id">投資組合的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 204 NoContent；
    /// 若投資組合不存在則返回 404。
    /// </returns>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePortfolio(Guid id, CancellationToken cancellationToken)
    {
        var result = await _portfolioService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }
}
