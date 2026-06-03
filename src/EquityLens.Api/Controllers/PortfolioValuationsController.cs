using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/valuation")]
public class PortfolioValuationsController : ApiControllerBase
{
    private readonly IPortfolioValuationService _valuationService;

    public PortfolioValuationsController(IPortfolioValuationService valuationService)
    {
        _valuationService = valuationService;
    }

    [HttpGet]
    public async Task<ActionResult<PortfolioValuationResponse>> GetValuation(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _valuationService.GetValuationAsync(portfolioId, cancellationToken);
        return ToActionResult(result);
    }
}
