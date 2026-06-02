using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Services.PortfolioHoldings;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/holdings")]
public class PortfolioHoldingsController : ControllerBase
{
    private readonly IPortfolioHoldingService _holdingService;

    public PortfolioHoldingsController(IPortfolioHoldingService holdingService)
    {
        _holdingService = holdingService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioHoldingResponse>>> GetHoldings(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.ListAsync(portfolioId, cancellationToken);
        return ToActionResult(result);
    }

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

    [HttpDelete("{holdingId:guid}")]
    public async Task<IActionResult> DeleteHolding(
        Guid portfolioId,
        Guid holdingId,
        CancellationToken cancellationToken)
    {
        var result = await _holdingService.DeleteAsync(portfolioId, holdingId, cancellationToken);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
        return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
            ? NotFound(error)
            : BadRequest(error);
    }

    private IActionResult ToErrorActionResult<T>(Result<T> result)
    {
        var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
        return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
            ? NotFound(error)
            : BadRequest(error);
    }
}
