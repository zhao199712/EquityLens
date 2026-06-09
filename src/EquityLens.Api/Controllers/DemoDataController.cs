using EquityLens.Api.Contracts.DemoData;
using EquityLens.Api.Services.DemoData;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 演示資料控制器，提供演示資料的狀態查詢、種子資料建立與清除功能。
/// 僅在開發環境中可用。
/// </summary>
[ApiController]
[Route("api/demo-data")]
public sealed class DemoDataController : ControllerBase
{
    private readonly IDemoDataService _demoDataService;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 初始化演示資料控制器。
    /// </summary>
    /// <param name="demoDataService">演示資料服務。</param>
    /// <param name="environment">Web 主機環境資訊。</param>
    public DemoDataController(IDemoDataService demoDataService, IWebHostEnvironment environment)
    {
        _demoDataService = demoDataService;
        _environment = environment;
    }

    /// <summary>
    /// 取得當前演示資料的種子狀態。
    /// 僅在開發環境中返回資料，否則返回 404。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>演示資料狀態回應；非開發環境則返回 404。</returns>
    [HttpGet("status")]
    public async Task<ActionResult<DemoDataStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.GetStatusAsync(cancellationToken));
    }

    /// <summary>
    /// 建立演示資料，包含預設投資組合、持倉、證券與市場價格。
    /// 僅在開發環境中執行，否則返回 404。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>種子資料建立結果；非開發環境則返回 404。</returns>
    [HttpPost("seed")]
    public async Task<ActionResult<SeedDemoDataResponse>> Seed(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.SeedAsync(cancellationToken));
    }

    /// <summary>
    /// 清除所有演示資料。
    /// 僅在開發環境中執行，否則返回 404。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>清除結果；非開發環境則返回 404。</returns>
    [HttpDelete]
    public async Task<ActionResult<ClearDemoDataResponse>> Clear(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.ClearAsync(cancellationToken));
    }
}
