using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Services.Securities;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecuritiesController : ControllerBase
{
    private readonly ISecurityService _securityService;

    public SecuritiesController(ISecurityService securityService)
    {
        _securityService = securityService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SecurityResponse>>> GetSecurities(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var securities = await _securityService.SearchAsync(query, cancellationToken);
        return Ok(securities);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SecurityResponse>> GetSecurity(Guid id, CancellationToken cancellationToken)
    {
        var result = await _securityService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

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
}
