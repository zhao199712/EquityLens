using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Services.Portfolios;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfoliosController : ApiControllerBase
{
    private readonly IPortfolioService _portfolioService;

    public PortfoliosController(IPortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioListItemResponse>>> GetPortfolios(CancellationToken cancellationToken)
    {
        var portfolios = await _portfolioService.ListAsync(cancellationToken);
        return Ok(portfolios);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioDetailResponse>> GetPortfolio(Guid id, CancellationToken cancellationToken)
    {
        var result = await _portfolioService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

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

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePortfolio(
        Guid id,
        UpdatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _portfolioService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePortfolio(Guid id, CancellationToken cancellationToken)
    {
        var result = await _portfolioService.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

}
