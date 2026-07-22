using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/agent-workflows")]
public sealed class AdminAgentWorkflowsController(IAgentWorkflowAdminService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<AgentWorkflowAdminResponse>> List(CancellationToken ct) => service.ListWorkflowsAsync(ct);
    [HttpPut("{workflowType}")]
    public async Task<ActionResult<AgentWorkflowAdminResponse>> Update(string workflowType, UpdateAgentWorkflowSettingRequest request, CancellationToken ct) =>
        (await service.UpdateWorkflowAsync(workflowType, request, ct)) is { } item ? Ok(item) : NotFound();
}

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/agent-nodes")]
public sealed class AdminAgentNodesController(IAgentWorkflowAdminService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<AgentNodeAdminResponse>> List(CancellationToken ct) => service.ListNodesAsync(ct);
    [HttpPut("{nodeType}")]
    public async Task<ActionResult<AgentNodeAdminResponse>> Update(string nodeType, UpdateAgentNodeSettingRequest request, CancellationToken ct)
    {
        try { return (await service.UpdateNodeAsync(nodeType, request, ct)) is { } item ? Ok(item) : NotFound(); }
        catch (ArgumentOutOfRangeException) { return BadRequest(new { code = "invalid_execution_policy" }); }
    }
}

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/agent-registry")]
public sealed class AdminAgentRegistryController(
    IWorkflowSkillCatalog skills,
    INodeCapabilityRegistry capabilities) : ControllerBase
{
    [HttpGet]
    public AgentRegistryAdminResponse List() => new(skills.Skills, capabilities.Capabilities);
}
