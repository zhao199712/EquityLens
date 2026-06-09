using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.Securities;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 證券控制器，提供證券查詢、搜尋、解析、刷新與批次刷新價格功能。
/// 所有證券資料以外部 API 為唯一事實來源。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SecuritiesController : ApiControllerBase
{
    private readonly ISecurityService _securityService;
    private readonly IMarketPriceService _marketPriceService;

    /// <summary>
    /// 初始化證券控制器。
    /// </summary>
    /// <param name="securityService">證券服務。</param>
    /// <param name="marketPriceService">市場價格服務。</param>
    public SecuritiesController(ISecurityService securityService, IMarketPriceService marketPriceService)
    {
        _securityService = securityService;
        _marketPriceService = marketPriceService;
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
    /// 解析證券資料：若本地已存在則返回現有資料（metadata 過期時自動刷新），
    /// 否則查詢外部 API 並建立新證券。
    /// </summary>
    /// <param name="request">解析證券的請求資料，只需提供 SecurityId 或 Ticker+Exchange。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回解析結果（包含是否為新建立）；
    /// 若必填欄位缺失則返回錯誤碼 <c>security.required_fields</c>；
    /// 若證券不存在於本地與外部則返回錯誤碼 <c>security.not_found</c>。
    /// </returns>
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveSecurityResponse>> ResolveSecurity(
        ResolveSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _securityService.ResolveAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 刷新資料庫中既有證券的 metadata，從外部 API 取得最新資料並更新。
    /// </summary>
    /// <param name="force">是否強制刷新所有證券，不論是否過期（預設 false）。</param>
    /// <param name="limit">最多處理的證券數量（預設 100）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>刷新結果統計。</returns>
    [HttpPost("refresh-all")]
    public async Task<ActionResult<RefreshSecuritiesResponse>> RefreshAllSecurities(
        [FromQuery] bool force = false,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _securityService.RefreshAllAsync(force, limit, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 批次刷新資料庫中既有證券的價格資料。
    /// 若今日已同步且未強制刷新，則略過。
    /// </summary>
    /// <param name="days">拉取最近幾天的日線資料（預設 365）。</param>
    /// <param name="force">是否強制刷新，忽略今日已同步的檢查（預設 false）。</param>
    /// <param name="limit">最多處理的證券數量（預設 100）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>批次刷新結果統計。</returns>
    [HttpPost("refresh-all-prices")]
    public async Task<ActionResult<RefreshSecuritiesPricesResponse>> RefreshAllPrices(
        [FromQuery] int days = 365,
        [FromQuery] bool force = false,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _marketPriceService.RefreshAllPricesAsync(days, force, limit, cancellationToken);
        return Ok(result);
    }
}
