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
    private readonly IAgentApprovalService? _approvalService;
    private bool IsAdmin => HttpContext?.User?.IsInRole("Admin") == true;

    public AgentRunsController(IAgentRunService agentRunService, ICurrentUserContext currentUser, IAgentApprovalService? approvalService = null)
    {
        _agentRunService = agentRunService;
        _currentUser = currentUser;
        _approvalService = approvalService;
    }

    [HttpPost("/api/portfolios/{portfolioId:guid}/agent-diagnoses")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreatePortfolioDiagnosis(
        Guid portfolioId, CreatePortfolioDiagnosisRequest request, CancellationToken cancellationToken)
    {
        var response = await _agentRunService.CreatePortfolioDiagnosisAsync(
            _currentUser.UserId, portfolioId, request.From, request.To, cancellationToken: cancellationToken);
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
    /// 從需要更多證據的 CriticReview 建立 EvidenceRemediation workflow。
    /// </summary>
    [HttpPost("evidence-remediation")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreateEvidenceRemediation(
        CreateEvidenceRemediationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CriticReviewRunId == Guid.Empty)
        {
            return BadRequest(new ApiError("critic_review_run_id_required", "請指定 criticReviewRunId。"));
        }
        var response = await _agentRunService.CreateEvidenceRemediationAsync(_currentUser.UserId, request.CriticReviewRunId, cancellationToken);
        return Ok(response);
    }

    /// <summary>從需要重新分析的 EvidenceRemediation 建立受控重新分析流程。</summary>
    [HttpPost("evidence-reanalysis")]
    public async Task<ActionResult<AgentRunSummaryResponse>> CreateEvidenceReanalysis(
        CreateEvidenceReanalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EvidenceRemediationRunId == Guid.Empty)
            return BadRequest(new ApiError("evidence_remediation_run_id_required", "請指定 evidenceRemediationRunId。"));
        try
        {
            var response = await _agentRunService.CreateEvidenceReanalysisAsync(_currentUser.UserId, request.EvidenceRemediationRunId, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException exception) when (exception.Message == "Evidence remediation run not found.")
        {
            return NotFound(new ApiError("evidence_remediation_run_not_found", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ApiError("evidence_reanalysis_invalid_source", exception.Message));
        }
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
        var runs = await _agentRunService.ListAsync(IsAdmin ? null : _currentUser.UserId, limit, workflowType, status, researchRunId, cancellationToken);
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
        var detail = await _agentRunService.GetByIdAsync(runId, IsAdmin ? null : _currentUser.UserId, cancellationToken);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    [HttpPost("{runId:guid}/feedback")]
    public async Task<ActionResult<SubmitAgentFeedbackResponse>> SubmitFeedback(
        Guid runId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _agentRunService.SubmitFeedbackAsync(runId, _currentUser.UserId, request, cancellationToken));
        }
        catch (AgentFeedbackException exception) when (exception.Code == "agent_run_not_found")
        {
            return NotFound(new ApiError(exception.Code, exception.Message));
        }
        catch (AgentFeedbackException exception)
        {
            return BadRequest(new ApiError(exception.Code, exception.Message));
        }
    }

    [HttpGet("{runId:guid}/children")]
    public async Task<ActionResult<IReadOnlyList<AgentRunSummaryResponse>>> ListChildren(
        Guid runId, CancellationToken cancellationToken = default) =>
        Ok(await _agentRunService.ListChildrenAsync(runId, _currentUser.UserId, cancellationToken));

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

    [HttpPost("{runId:guid}/approvals/{approvalId:guid}/approve")]
    public async Task<ActionResult<AgentApprovalResponse>> Approve(
        Guid runId, Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default) =>
        await DecideApproval(runId, approvalId, request, approve: true, cancellationToken);

    [HttpPost("{runId:guid}/approvals/{approvalId:guid}/reject")]
    public async Task<ActionResult<AgentApprovalResponse>> Reject(
        Guid runId, Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default) =>
        await DecideApproval(runId, approvalId, request, approve: false, cancellationToken);

    private async Task<ActionResult<AgentApprovalResponse>> DecideApproval(
        Guid runId, Guid approvalId, DecideAgentApprovalRequest request, bool approve, CancellationToken cancellationToken)
    {
        if (_approvalService is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        try
        {
            var result = approve
                ? await _approvalService.ApproveAsync(runId, approvalId, _currentUser.UserId, IsAdmin, request, cancellationToken)
                : await _approvalService.RejectAsync(runId, approvalId, _currentUser.UserId, IsAdmin, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (AgentApprovalException exception) when (exception.Code is "approval_already_decided" or "approval_state_conflict")
        {
            return Conflict(new ApiError(exception.Code, exception.Message));
        }
        }
        catch (AgentApprovalException exception)
        {
            return BadRequest(new ApiError(exception.Code, exception.Message));
        }
    }
}
