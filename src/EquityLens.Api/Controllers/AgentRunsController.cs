using EquityLens.Api.Contracts.AgentRun;
using EquityLens.Api.Services.AgentRuns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// Agent Run 管理 API，提供 CriticReview workflow 的建立、查詢、重試與取消。
/// </summary>
[Authorize]
[ApiController]
[Route("api/agent-runs")]
public sealed class AgentRunsController : ApiControllerBase
{
    private readonly IAgentRunService _agentRunService;

    public AgentRunsController(IAgentRunService agentRunService)
    {
        _agentRunService = agentRunService;
    }

    /// <summary>
    /// 建立一個 CriticReview agent run。
    /// </summary>
    [HttpPost("critic-review")]
    public async Task<ActionResult<AgentRunCreatedResponse>> CreateCriticReview(
        CreateCriticReviewRequest request, CancellationToken ct)
    {
        var result = await _agentRunService.CreateCriticReviewAsync(request, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// 列出 agent runs，支援依 workflowType 和 status 篩選。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentRunListItemResponse>>> ListRuns(
        [FromQuery] int limit = 20,
        [FromQuery] string? workflowType = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var runs = await _agentRunService.ListAsync(limit, workflowType, status, ct);
        return Ok(runs);
    }

    /// <summary>
    /// 取得 agent run 詳情，包含 nodes、events、toolCalls。
    /// </summary>
    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<AgentRunDetailResponse>> GetRunDetail(
        Guid runId, CancellationToken ct)
    {
        var result = await _agentRunService.GetDetailAsync(runId, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// 重試一個 Failed 狀態的 agent run。
    /// </summary>
    [HttpPost("{runId:guid}/retry")]
    public async Task<ActionResult<AgentRunCreatedResponse>> RetryRun(
        Guid runId, CancellationToken ct)
    {
        var result = await _agentRunService.RetryAsync(runId, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// 取消一個正在進行的 agent run。
    /// </summary>
    [HttpPost("{runId:guid}/cancel")]
    public async Task<IActionResult> CancelRun(Guid runId, CancellationToken ct)
    {
        var result = await _agentRunService.CancelAsync(runId, ct);
        return result.IsSuccess ? NoContent() : ToErrorActionResult(result);
    }
}
