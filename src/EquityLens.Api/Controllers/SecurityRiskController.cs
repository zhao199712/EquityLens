using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 個股風險分析控制器，提供 GBM 參數估計、歷史與 Monte Carlo VaR/ES 計算。
/// </summary>
[Authorize]
[ApiController]
[Route("api/securities/{securityId:guid}/risk")]
public sealed class SecurityRiskController : ApiControllerBase
{
    private readonly IRiskAnalysisService _riskAnalysisService;

    /// <summary>
    /// 初始化個股風險分析控制器。
    /// </summary>
    /// <param name="riskAnalysisService">風險分析服務。</param>
    public SecurityRiskController(IRiskAnalysisService riskAnalysisService)
    {
        _riskAnalysisService = riskAnalysisService;
    }

    /// <summary>
    /// 計算指定證券的風險指標，包含 GBM 參數（漂移與波動率）、歷史與 Monte Carlo VaR/ES。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="from">歷史價格起始日期。</param>
    /// <param name="to">歷史價格結束日期。</param>
    /// <param name="horizonDays">模擬天數（預設 30）。</param>
    /// <param name="confidenceLevel">信心水準（預設 0.95）。</param>
    /// <param name="simulations">蒙地卡羅模擬路徑數量（預設 10000）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回風險分析結果；
    /// 若證券不存在則返回 404；
    /// 若價格資料不足或參數無效則返回 400。
    /// </returns>
    [HttpGet]
    public async Task<ActionResult<SecurityRiskResponse>> GetSecurityRisk(
        Guid securityId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int horizonDays = 30,
        [FromQuery] decimal confidenceLevel = 0.95m,
        [FromQuery] int simulations = 10000,
        CancellationToken cancellationToken = default)
    {
        var result = await _riskAnalysisService.GetSecurityRiskAsync(
            securityId, from, to, horizonDays, confidenceLevel, simulations, cancellationToken);
        return ToActionResult(result);
    }
}
