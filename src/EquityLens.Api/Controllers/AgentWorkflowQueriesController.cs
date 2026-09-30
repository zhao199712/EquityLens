using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EquityLens.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/agent-queries")]
public sealed class AgentWorkflowQueriesController(IAgentWorkflowQueryService service, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Llm)]
    public async Task<ActionResult<AgentWorkflowQueryCreatedResponse>> Create(CreateAgentWorkflowQueryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Accepted(await service.CreateAsync(currentUser.UserId, request, cancellationToken));
        }
        catch (AgentWorkflowQueryException exception)
        {
            return BadRequest(new ApiError(exception.Code, exception.Message));
        }
    }
}
