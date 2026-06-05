using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/market-prices")]
public sealed class MarketPriceImportsController : ApiControllerBase
{
    private readonly IMarketPriceService _marketPriceService;

    public MarketPriceImportsController(IMarketPriceService marketPriceService)
    {
        _marketPriceService = marketPriceService;
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportMarketPricesByTickerResponse>> ImportPrices(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.ImportDailyPricesByTickerAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}
