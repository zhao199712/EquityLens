using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Services.Securities;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 證券控制器，提供證券查詢、搜尋、建立與解析功能。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SecuritiesController : ApiControllerBase
{
    private readonly ISecurityService _securityService;

    /// <summary>
    /// 初始化證券控制器。
    /// </summary>
    /// <param name="securityService">證券服務。</param>
    public SecuritiesController(ISecurityService securityService)
    {
        _securityService = securityService;
    }

    /// <summary>
    /// 依據關鍵字搜尋本地已存在的證券。
    /// </summary>
    /// <param name="query">搜尋關鍵字（選填）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>符合條件的證券列表。</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SecurityResponse>>> GetSecurities(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var securities = await _securityService.SearchAsync(query, cancellationToken);
        return Ok(securities);
    }

    /// <summary>
    /// 搜尋本地與外部資料來源可用的證券，並合併去重後返回。
    /// </summary>
    /// <param name="query">搜尋關鍵字（選填）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>合併後的證券搜尋結果列表，最多返回 25 筆。</returns>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<SecuritySearchResult>>> SearchSecurities(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var securities = await _securityService.SearchAvailableAsync(query, cancellationToken);
        return Ok(securities);
    }

    /// <summary>
    /// 依據證券識別碼取得證券詳細資料。
    /// </summary>
    /// <param name="id">證券的唯一識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>證券詳細資料；若不存在則返回 404。</returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SecurityResponse>> GetSecurity(Guid id, CancellationToken cancellationToken)
    {
        var result = await _securityService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 解析證券資料：若已存在則返回現有資料，否則自動建立新證券。
    /// </summary>
    /// <param name="request">解析證券的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>解析結果；若必填欄位缺失則返回 400。</returns>
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveSecurityResponse>> ResolveSecurity(
        ResolveSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _securityService.ResolveAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 建立新的證券資料。
    /// </summary>
    /// <param name="request">建立證券的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回 201 Created 與證券資料；
    /// 若必填欄位缺失則返回 400；
    /// 若證券已存在則返回 409 Conflict。
    /// </returns>
    [HttpPost]
    public async Task<ActionResult<SecurityResponse>> CreateSecurity(
        CreateSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _securityService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorCode == "security.duplicate"
                ? Conflict(new ApiError(result.ErrorCode, result.ErrorMessage!))
                : ToActionResult(result);
        }

        return CreatedAtAction(nameof(GetSecurity), new { id = result.Value!.Id }, result.Value);
    }
}
