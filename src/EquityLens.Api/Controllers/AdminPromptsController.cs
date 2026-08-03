using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

/// <summary>管理 V3 Agent 的 versioned prompts、binding 與稽核紀錄。</summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/prompts")]
public sealed class AdminPromptsController(IPromptManagementService prompts, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<PromptTemplateResponse>> List(CancellationToken ct) => prompts.ListTemplatesAsync(ct);
    [HttpGet("{templateId:guid}")] public async Task<ActionResult<PromptTemplateResponse>> Get(Guid templateId, CancellationToken ct) => (await prompts.GetTemplateAsync(templateId, ct)) is { } item ? Ok(item) : NotFound();
    [HttpPost] public Task<ActionResult> Create(CreatePromptTemplateRequest request, CancellationToken ct) => Execute(async () => { var result = await prompts.CreateTemplateAsync(request, currentUser.UserId, ct); return CreatedAtAction(nameof(Get), new { templateId = result.Id }, result); });
    [HttpPost("{templateId:guid}/versions")] public Task<ActionResult> CreateDraft(Guid templateId, CreatePromptVersionRequest request, CancellationToken ct) => Execute(async () => Ok(await prompts.CreateDraftAsync(templateId, request, currentUser.UserId, ct)));
    [HttpPut("versions/{versionId:guid}")] public Task<ActionResult> UpdateDraft(Guid versionId, UpdatePromptVersionRequest request, CancellationToken ct) => Execute(async () => Ok(await prompts.UpdateDraftAsync(versionId, request, currentUser.UserId, ct)));
    [HttpGet("versions/{versionId:guid}/content")] public async Task<ActionResult<PromptVersionContentResponse>> Content(Guid versionId, CancellationToken ct) => (await prompts.GetVersionContentAsync(versionId, ct)) is { } result ? Ok(result) : NotFound();
    [HttpPost("versions/{versionId:guid}/publish")] public Task<ActionResult> Publish(Guid versionId, PublishPromptVersionRequest request, CancellationToken ct) => Execute(async () => Ok(await prompts.PublishAsync(versionId, request, currentUser.UserId, ct)));
    [HttpPost("versions/{versionId:guid}/rollback")] public Task<ActionResult> Rollback(Guid versionId, RollbackPromptVersionRequest request, CancellationToken ct) => Execute(async () => Ok(await prompts.RollbackAsync(versionId, request, currentUser.UserId, ct)));
    [HttpPost("{templateId:guid}/disable")] public Task<ActionResult> Disable(Guid templateId, DisablePromptTemplateRequest request, CancellationToken ct) => Execute(async () => { await prompts.DisableTemplateAsync(templateId, request, currentUser.UserId, ct); return NoContent(); });
    [HttpGet("bindings")] public Task<IReadOnlyList<PromptBindingResponse>> Bindings(CancellationToken ct) => prompts.ListBindingsAsync(ct);
    [HttpPut("bindings/{usageKey}")] public Task<ActionResult> UpdateBinding(string usageKey, UpdatePromptBindingRequest request, CancellationToken ct) => Execute(async () => (await prompts.UpdateBindingAsync(usageKey, request, currentUser.UserId, ct)) is { } b ? Ok(b) : NotFound());
    [HttpGet("audit")] public Task<IReadOnlyList<PromptAuditLogResponse>> Audit([FromQuery] Guid? templateId, CancellationToken ct) => prompts.ListAuditAsync(templateId, ct);

    private async Task<ActionResult> Execute(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (PromptManagementException ex) { return Conflict(new { code = ex.Code, message = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "prompt_concurrency_conflict" }); }
    }
}
