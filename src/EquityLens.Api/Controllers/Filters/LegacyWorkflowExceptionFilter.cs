using EquityLens.Api.Common;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EquityLens.Api.Controllers.Filters;

public sealed class LegacyWorkflowExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not LegacyWorkflowRetiredException error) return;
        context.Result = new ConflictObjectResult(new ApiError(LegacyWorkflowRetiredException.Code, error.Message));
        context.ExceptionHandled = true;
    }
}
