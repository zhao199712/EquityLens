using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 市場價格導入控制器，提供依據股票代號與交易所導入每日價格的功能。
/// </summary>
[ApiController]
[Route("api/market-prices")]
public sealed class MarketPriceImportsController : ApiControllerBase
{
    private readonly IMarketPriceService _marketPriceService;

    /// <summary>
    /// 初始化市場價格導入控制器。
    /// </summary>
    /// <param name="marketPriceService">市場價格服務。</param>
    public MarketPriceImportsController(IMarketPriceService marketPriceService)
    {
        _marketPriceService = marketPriceService;
    }

    /// <summary>
    /// 依據股票代號與交易所導入每日市場價格；若證券不存在則自動建立。
    /// </summary>
    /// <param name="request">依代號導入價格的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>導入結果；若缺少必要欄位、交易所不支援或資料提供者發生錯誤則返回對應錯誤。</returns>
    [HttpPost("import")]
    public async Task<ActionResult<ImportMarketPricesByTickerResponse>> ImportPrices(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.ImportDailyPricesByTickerAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}
