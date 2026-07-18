using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// Agent 執行紀錄控制器，提供 V3 workflow 建立、查詢與操作能力。
/// </summary>
[Authorize]
[ApiController]
[Route("api/agent-runs")]
public sealed class AgentRunsController : ControllerBase
{
    private readonly IAgentRunService _agentRunService;
    private readonly ICurrentUserContext _currentUser;

    public AgentRunsController(IAgentRunService agentRunService, ICurrentUserContext currentUser)
    {
        _agentRunService = agentRunService;
        _currentUser = currentUser;
    }

    [HttpPost("/api/portfolios/{portfolioId:guid}/agent-diagnoses")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreatePortfolioDiagnosis(
        Guid portfolioId, CreatePortfolioDiagnosisRequest request, CancellationToken cancellationToken)
    {
        var response = await _agentRunService.CreatePortfolioDiagnosisAsync(
            _currentUser.UserId, portfolioId, request.From, request.To, cancellationToken);
        return Accepted(response);
    }

    /// <summary>
    /// 建立並執行 CriticReview workflow，檢查既有 research run 的回答品質與證據覆蓋。
    /// </summary>
    [HttpPost("critic-review")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreateCriticReview(
        CreateCriticReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ResearchRunId == Guid.Empty)
        {
            return BadRequest(new ApiError("research_run_id_required", "請指定 researchRunId。"));
        }

        var response = await _agentRunService.CreateCriticReviewAsync(
            _currentUser.UserId,
            request.ResearchRunId,
            cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 建立並執行 DraftRevision workflow，根據 CriticReview 結果產生修訂稿。
    /// </summary>
    [HttpPost("draft-revision")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreateDraftRevision(
        CreateDraftRevisionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CriticReviewRunId == Guid.Empty)
        {
            return BadRequest(new ApiError("critic_review_run_id_required", "請指定 criticReviewRunId。"));
        }

        var response = await _agentRunService.CreateDraftRevisionAsync(
            _currentUser.UserId,
            request.CriticReviewRunId,
            cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 建立並執行 Research Quality Review workflow，合併 CriticReview 與 DraftRevision 為單一完整流程。
    /// </summary>
    [HttpPost("research-quality-review")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreateResearchQualityReview(
        CreateResearchQualityReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ResearchRunId == Guid.Empty)
        {
            return BadRequest(new ApiError("research_run_id_required", "請指定 researchRunId。"));
        }

        var response = await _agentRunService.CreateResearchQualityReviewAsync(
            _currentUser.UserId,
            request.ResearchRunId,
            cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 查詢 Agent runs 清單，可依 workflow type 與狀態篩選。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentRunSummaryResponse>>> List(
        [FromQuery] int limit = 50,
        [FromQuery] string? workflowType = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? researchRunId = null,
        CancellationToken cancellationToken = default)
    {
        var runs = await _agentRunService.ListAsync(_currentUser.UserId, limit, workflowType, status, researchRunId, cancellationToken);
        return Ok(runs);
    }

    /// <summary>
    /// 查詢 Agent run 詳細資料，包含 nodes、events、tool calls、blackboard 與 output。
    /// </summary>
    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<AgentRunDetailResponse>> GetDetail(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var detail = await _agentRunService.GetByIdAsync(runId, _currentUser.UserId, cancellationToken);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    /// <summary>
    /// 重新執行失敗的 Agent run。第一版以整個 run 為 retry 單位。
    /// </summary>
    [HttpPost("{runId:guid}/retry")]
    public async Task<ActionResult<AgentRunSummaryResponse>> Retry(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await _agentRunService.RetryAsync(runId, _currentUser.UserId, cancellationToken);
        if (response is null) return NotFound();
        return Ok(response);
    }

    /// <summary>
    /// 取消 Agent run。
    /// </summary>
    [HttpPost("{runId:guid}/cancel")]
    public async Task<ActionResult<AgentRunSummaryResponse>> Cancel(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await _agentRunService.CancelAsync(runId, _currentUser.UserId, cancellationToken);
        if (response is null) return NotFound();
        return Ok(response);
    }
}
