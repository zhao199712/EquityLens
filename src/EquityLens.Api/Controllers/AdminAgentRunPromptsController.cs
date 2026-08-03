using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/agent-runs/{runId:guid}/prompt-snapshots")]
public sealed class AdminAgentRunPromptsController(IPromptManagementService prompts) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<AgentRunPromptSnapshotResponse>> List(Guid runId, CancellationToken ct) => prompts.ListRunSnapshotsAsync(runId, ct);
    [HttpGet("{snapshotId:guid}/content")]
    public async Task<ActionResult<AgentRunPromptSnapshotContentResponse>> Content(Guid runId, Guid snapshotId, CancellationToken ct) => (await prompts.GetRunSnapshotContentAsync(runId, snapshotId, ct)) is { } result ? Ok(result) : NotFound();
}
