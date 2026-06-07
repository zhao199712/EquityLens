using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 市場價格控制器，提供指定證券的歷史價格查詢與每日價格導入/同步功能。
/// </summary>
[ApiController]
[Route("api/securities/{securityId:guid}/prices")]
public sealed class MarketPricesController : ApiControllerBase
{
    private readonly IMarketPriceService _marketPriceService;

    /// <summary>
    /// 初始化市場價格控制器。
    /// </summary>
    /// <param name="marketPriceService">市場價格服務。</param>
    public MarketPricesController(IMarketPriceService marketPriceService)
    {
        _marketPriceService = marketPriceService;
    }

    /// <summary>
    /// 查詢指定證券在日期區間內的市場價格。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="from">起始日期（選填）。</param>
    /// <param name="to">結束日期（選填）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>價格列表；若證券不存在或日期區間無效則返回對應錯誤。</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketPriceResponse>>> GetPrices(
        Guid securityId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.GetPricesAsync(securityId, from, to, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 為指定證券導入指定日期區間的每日市場價格。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="request">導入價格的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>導入結果；若證券不存在、交易所不支援或資料提供者發生錯誤則返回對應錯誤。</returns>
    [HttpPost("import")]
    public async Task<ActionResult<ImportMarketPricesResponse>> ImportPrices(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.ImportDailyPricesAsync(securityId, request, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// 同步指定證券的每日市場價格（目前行為與導入相同）。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="request">同步價格的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>同步結果；可能的錯誤與導入相同。</returns>
    [HttpPost("sync")]
    public async Task<ActionResult<ImportMarketPricesResponse>> SyncPrices(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.SyncDailyPricesAsync(securityId, request, cancellationToken);
        return ToActionResult(result);
    }
}
