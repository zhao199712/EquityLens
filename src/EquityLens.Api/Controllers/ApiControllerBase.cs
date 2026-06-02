using EquityLens.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
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

    protected IActionResult ToErrorActionResult<T>(Result<T> result)
    {
        var error = new ApiError(result.ErrorCode!, result.ErrorMessage!);
        return result.ErrorCode!.EndsWith("not_found", StringComparison.Ordinal)
            ? NotFound(error)
            : BadRequest(error);
    }
}
