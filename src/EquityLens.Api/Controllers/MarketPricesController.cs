using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/securities/{securityId:guid}/prices")]
public sealed class MarketPricesController : ApiControllerBase
{
    private readonly IMarketPriceService _marketPriceService;

    public MarketPricesController(IMarketPriceService marketPriceService)
    {
        _marketPriceService = marketPriceService;
    }

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

    [HttpPost("import")]
    public async Task<ActionResult<ImportMarketPricesResponse>> ImportPrices(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _marketPriceService.ImportDailyPricesAsync(securityId, request, cancellationToken);
        return ToActionResult(result);
    }
}
