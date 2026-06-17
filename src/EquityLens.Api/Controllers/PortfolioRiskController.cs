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
    private readonly ICurrentUserContext _currentUser;

    /// <summary>
    /// 初始化投資組合風險分析控制器。
    /// </summary>
    /// <param name="riskAnalysisService">風險分析服務。</param>
    /// <param name="currentUser">目前使用者內容。</param>
    public PortfolioRiskController(
        IRiskAnalysisService riskAnalysisService,
        ICurrentUserContext currentUser)
    {
        _riskAnalysisService = riskAnalysisService;
        _currentUser = currentUser;
    }

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
}
