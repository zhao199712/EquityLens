using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 投資組合風險分析控制器，提供歷史 VaR、ES、波動率、夏普比率與最大回撤計算。
/// </summary>
[Authorize]
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/risk")]
public sealed class PortfolioRiskController : ApiControllerBase
{
    private readonly IRiskAnalysisService _riskAnalysisService;
    private readonly IRiskBacktestRunService _riskBacktestRunService;
    private readonly IRiskCalculationRunService _riskCalculationRunService;
    private readonly ICurrentUserContext _currentUser;

    /// <summary>
    /// 初始化投資組合風險分析控制器。
    /// </summary>
    /// <param name="riskAnalysisService">風險分析服務。</param>
    /// <param name="currentUser">目前使用者內容。</param>
    public PortfolioRiskController(
        IRiskAnalysisService riskAnalysisService,
        IRiskBacktestRunService riskBacktestRunService,
        IRiskCalculationRunService riskCalculationRunService,
        ICurrentUserContext currentUser)
    {
        _riskAnalysisService = riskAnalysisService;
        _riskBacktestRunService = riskBacktestRunService;
        _riskCalculationRunService = riskCalculationRunService;
        _currentUser = currentUser;
    }

    /// <summary>建立由 Redis 交付至 EquityLens.Mathematics 的正式非同步風險計算。</summary>
    [HttpPost("calculations")]
    public async Task<ActionResult<RiskCalculationRunResponse>> CreateRiskCalculation(
        Guid portfolioId,
        [FromBody] CreateRiskCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _riskCalculationRunService.CreateAsync(
            portfolioId, request, _currentUser.UserId, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        return AcceptedAtAction(
            nameof(GetRiskCalculation),
            new { portfolioId, runId = result.Value!.Id },
            result.Value);
    }

    /// <summary>取得正式非同步風險計算狀態與結果。</summary>
    [HttpGet("calculations/{runId:guid}")]
    public async Task<ActionResult<RiskCalculationRunResponse>> GetRiskCalculation(
        Guid portfolioId, Guid runId, CancellationToken cancellationToken = default)
    {
        var result = await _riskCalculationRunService.GetAsync(
            portfolioId, runId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>列出目前使用者的近期正式風險計算。</summary>
    [HttpGet("calculations")]
    public async Task<ActionResult<IReadOnlyList<RiskCalculationRunResponse>>> ListRiskCalculations(
        Guid portfolioId, CancellationToken cancellationToken = default) =>
        Ok(await _riskCalculationRunService.ListAsync(
            portfolioId, _currentUser.UserId, cancellationToken));

    /// <summary>
    /// 計算指定投資組合的歷史與 Monte Carlo 風險指標，包含 VaR、ES、波動率、夏普比率與最大回撤。
    /// 使用 Historical Simulation + Correlated GBM Monte Carlo。
    /// </summary>
    /// <param name="portfolioId">投資組合的唯一識別碼。</param>
    /// <param name="from">歷史價格起始日期。</param>
    /// <param name="to">歷史價格結束日期。</param>
    /// <param name="horizonDays">相容舊版呼叫保留；回應固定包含 1、7、30 日期限。</param>
    /// <param name="confidenceLevel">信心水準（預設 0.95）。</param>
    /// <param name="simulations">蒙地卡羅模擬路徑數量（預設 10000）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回風險分析結果；
    /// 若投資組合不存在則返回 404；
    /// 若價格資料不足或參數無效則返回 400。
    /// </returns>
    [HttpGet]
    public async Task<ActionResult<PortfolioRiskResponse>> GetPortfolioRisk(
        Guid portfolioId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int horizonDays = 30,
        [FromQuery] decimal confidenceLevel = 0.95m,
        [FromQuery] int simulations = 10000,
        [FromQuery] string model = "gbm_ewma_normal",
        CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioRiskAsync(
            portfolioId, from, to, horizonDays, confidenceLevel, simulations,
            _currentUser.UserId, cancellationToken, model);
        return ToActionResult(result);
    }

    [HttpGet("backtest")]
    public async Task<ActionResult<PortfolioRiskBacktestResponse>> GetPortfolioRiskBacktest(
        Guid portfolioId, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioRiskBacktestAsync(
            portfolioId, from, to, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Queues a saved backtest so expensive rolling simulations do not block the detail page.</summary>
    [HttpPost("backtests")]
    public async Task<ActionResult<PortfolioRiskBacktestRunResponse>> CreatePortfolioRiskBacktestRun(
        Guid portfolioId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken = default)
    {
        var result = await _riskBacktestRunService.CreateAsync(portfolioId, from, to, _currentUser.UserId, cancellationToken);
        if (!result.IsSuccess) return ToActionResult(result);
        return AcceptedAtAction(nameof(GetPortfolioRiskBacktestRun), new { portfolioId, runId = result.Value!.Id }, result.Value);
    }

    [HttpGet("backtests")]
    public async Task<ActionResult<IReadOnlyList<PortfolioRiskBacktestRunResponse>>> GetPortfolioRiskBacktestRuns(
        Guid portfolioId, CancellationToken cancellationToken = default) =>
        Ok(await _riskBacktestRunService.ListAsync(portfolioId, _currentUser.UserId, cancellationToken));

    [HttpGet("backtests/{runId:guid}")]
    public async Task<ActionResult<PortfolioRiskBacktestRunResponse>> GetPortfolioRiskBacktestRun(
        Guid portfolioId, Guid runId, CancellationToken cancellationToken = default)
    {
        var result = await _riskBacktestRunService.GetAsync(portfolioId, runId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("monte-carlo")]
    public async Task<ActionResult<PortfolioMonteCarloResponse>> GetPortfolioMonteCarlo(
        Guid portfolioId, [FromQuery] string model = "mvewma_fhs", CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioMonteCarloAsync(
            portfolioId, _currentUser.UserId, cancellationToken, model);
        return ToActionResult(result);
    }

    [HttpGet("governance")]
    public async Task<ActionResult<PortfolioRiskGovernanceResponse>> GetPortfolioRiskGovernance(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioRiskGovernanceAsync(portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("scenario")]
    public async Task<ActionResult<PortfolioRiskScenarioResponse>> CalculatePortfolioRiskScenario(Guid portfolioId, [FromBody] PortfolioRiskScenarioRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.CalculatePortfolioRiskScenarioAsync(portfolioId, request, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("reports")]
    public async Task<ActionResult<PortfolioRiskReportSnapshotDetailResponse>> CreatePortfolioRiskReportSnapshot(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.CreatePortfolioRiskReportSnapshotAsync(portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("reports")]
    public async Task<ActionResult<IReadOnlyList<PortfolioRiskReportSnapshotListItemResponse>>> GetPortfolioRiskReportSnapshots(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioRiskReportSnapshotsAsync(portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("reports/{reportId:guid}")]
    public async Task<ActionResult<PortfolioRiskReportSnapshotDetailResponse>> GetPortfolioRiskReportSnapshot(Guid portfolioId, Guid reportId, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioRiskReportSnapshotAsync(portfolioId, reportId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("stress")]
    public async Task<ActionResult<PortfolioStressTestResponse>> GetPortfolioStressTest(
        Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetPortfolioStressTestAsync(
            portfolioId, _currentUser.UserId, cancellationToken);
        return ToActionResult(result);
    }
}
