using System.Diagnostics;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.Research;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 研究資料控制器，提供已向量化文件的語意檢索與 AI 問答能力。
/// </summary>
[Authorize]
[ApiController]
[Route("api/research")]
public sealed class ResearchController : ControllerBase
{
    private readonly IDocumentSearchService _documentSearchService;
    private readonly IResearchPreflightService _researchPreflightService;
    private readonly IResearchAnswerService _researchAnswerService;
    private readonly IResearchRunTraceService _traceService;
    private readonly IAgentRunService _agentRunService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ResearchController> _logger;

    public ResearchController(
        IDocumentSearchService documentSearchService,
        IResearchPreflightService researchPreflightService,
        IResearchAnswerService researchAnswerService,
        IResearchRunTraceService traceService,
        IAgentRunService agentRunService,
        ICurrentUserContext currentUser,
        ILogger<ResearchController> logger)
    {
        _documentSearchService = documentSearchService;
        _researchPreflightService = researchPreflightService;
        _researchAnswerService = researchAnswerService;
        _traceService = traceService;
        _agentRunService = agentRunService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// 依自然語言問題搜尋最相關的文件 chunks，可選擇依股票代號與文件類型篩選。
    /// </summary>
    /// <param name="request">語意搜尋請求。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpPost("search")]
    public async Task<ActionResult<DocumentSearchResponse>> Search(
        DocumentSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new ApiError("query_required", "請輸入查詢內容。"));
        }
        if (!IsValidDocumentType(request.DocumentType, allowAuto: true))
        {
            return BadRequest(new ApiError("invalid_document_type", "documentType 僅支援 AnnualReport 或 EarningsPresentation。"));
        }

        var response = await _documentSearchService.SearchAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 針對指定股票提問，系統會先搜尋相關文件 chunks，再由 AI 生成附來源引用的回答。
    /// </summary>
    /// <param name="request">問答請求。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpPost("ask")]
    public async Task<ActionResult<ResearchAskResponse>> Ask(
        ResearchAskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new ApiError("question_required", "請輸入問題。"));
        }
        if (string.IsNullOrWhiteSpace(request.Ticker))
        {
            return BadRequest(new ApiError("ticker_required", "請指定股票代號。"));
        }
        if (!IsValidDocumentType(request.DocumentType, allowAuto: true))
        {
            return BadRequest(new ApiError("invalid_document_type", "documentType 僅支援 AnnualReport 或 EarningsPresentation。若要自動規劃來源，請省略 documentType 或使用 retrievalMode=Auto。"));
        }

        var preflight = await _researchPreflightService.ValidateAskAsync(request, cancellationToken);
        if (!preflight.IsSuccess)
        {
            Activity.Current?.SetTag("research.preflight.status", "failed");
            Activity.Current?.SetTag("research.preflight.error_code", preflight.ErrorCode);
            Activity.Current?.SetTag("research.ticker", request.Ticker.Trim().ToUpperInvariant());

            _logger.LogInformation(
                "Research ask preflight failed with {ErrorCode} for ticker {Ticker}, retrieval mode {RetrievalMode}, document type {DocumentType}",
                preflight.ErrorCode,
                request.Ticker.Trim().ToUpperInvariant(),
                request.RetrievalMode,
                request.DocumentType);

            return BadRequest(new ApiError(preflight.ErrorCode!, preflight.ErrorMessage!));
        }

        Activity.Current?.SetTag("research.preflight.status", "passed");
        Activity.Current?.SetTag("research.ticker", preflight.Value!.Ticker);

        var response = await _researchAnswerService.AskAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>建立端到端研究調查流程並立即回傳背景執行識別碼。</summary>
    [HttpPost("investigations")]
    public async Task<ActionResult<ResearchInvestigationCreatedResponse>> CreateInvestigation(ResearchAskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question)) return BadRequest(new ApiError("question_required", "請輸入問題。"));
        if (string.IsNullOrWhiteSpace(request.Ticker)) return BadRequest(new ApiError("ticker_required", "請指定股票代號。"));
        if (!IsValidDocumentType(request.DocumentType, allowAuto: true)) return BadRequest(new ApiError("invalid_document_type", "documentType 僅支援 AnnualReport 或 EarningsPresentation。"));
        var created = await _agentRunService.CreateResearchInvestigationAsync(_currentUser.UserId, request, cancellationToken: cancellationToken);
        return Accepted(new ResearchInvestigationCreatedResponse(created.AgentRun.Id, created.ResearchRunId, created.AgentRun.WorkflowType, created.AgentRun.Status));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<ResearchRunSummaryDto>>> ListRuns(
        [FromQuery] int limit = 50,
        [FromQuery] string? ticker = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var runs = await _traceService.ListAsync(_currentUser.UserId, limit, ticker, status, cancellationToken);
        return Ok(runs);
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<ResearchRunDetailDto>> GetRunDetail(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var detail = await _traceService.GetByIdAsync(runId, _currentUser.UserId, cancellationToken);
        if (detail is null) return NotFound();
        return Ok(detail);
    }

    private static bool IsValidDocumentType(string? documentType, bool allowAuto)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return true;
        }

        var value = documentType.Trim();
        return (allowAuto && string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            || value is "AnnualReport" or "EarningsPresentation";
    }
}
