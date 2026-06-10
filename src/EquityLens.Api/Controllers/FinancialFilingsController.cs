using EquityLens.Api.Contracts.Filings;
using EquityLens.Api.Services.FinancialFilings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 財報檔案控制器，提供上市公司財報上傳、查詢與刪除功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/financial-filings")]
public sealed class FinancialFilingsController : ControllerBase
{
    private readonly IFinancialFilingService _filingService;

    /// <summary>
    /// 初始化財報檔案控制器。
    /// </summary>
    /// <param name="filingService">財報上傳服務。</param>
    public FinancialFilingsController(IFinancialFilingService filingService)
    {
        _filingService = filingService;
    }

    /// <summary>
    /// 上傳上市公司財報檔案，並建立 metadata 記錄。
    /// </summary>
    /// <param name="file">財報檔案（PDF、XLSX 等）。</param>
    /// <param name="securityId">關聯股票識別碼。</param>
    /// <param name="fiscalYear">財報所屬年度。</param>
    /// <param name="fiscalQuarter">財報所屬季度（年報不傳或留空）。</param>
    /// <param name="filingType">財報類型，例如 AnnualReport、QuarterlyReport、10-K、10-Q。</param>
    /// <param name="periodEndDate">報表截止日（選擇性）。</param>
    /// <param name="publishedAt">發佈日期（選擇性）。</param>
    /// <param name="language">語言代碼，預設 en（選擇性）。</param>
    /// <param name="currency">幣別代碼，例如 TWD、USD（選擇性）。</param>
    /// <param name="source">資料來源名稱（選擇性）。</param>
    /// <param name="sourceUrl">原始來源網址（選擇性）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>成功時返回 201 Created 與財報記錄回應資料。</returns>
    [HttpPost]
    public async Task<ActionResult<FinancialFilingResponse>> UploadFiling(
        IFormFile file,
        [FromForm] Guid securityId,
        [FromForm] int fiscalYear,
        [FromForm] int? fiscalQuarter,
        [FromForm] string filingType,
        [FromForm] DateOnly? periodEndDate,
        [FromForm] DateTime? publishedAt,
        [FromForm] string? language,
        [FromForm] string? currency,
        [FromForm] string? source,
        [FromForm] string? sourceUrl,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "file_required", message = "請選擇要上傳的財報檔案。" });
        }

        if (securityId == Guid.Empty)
        {
            return BadRequest(new { error = "security_id_required", message = "請指定關聯的股票。" });
        }

        if (fiscalYear < 1900 || fiscalYear > 2100)
        {
            return BadRequest(new { error = "invalid_fiscal_year", message = "請輸入有效的財報年度。" });
        }

        var response = await _filingService.UploadAsync(
            file, securityId, fiscalYear, fiscalQuarter, filingType,
            periodEndDate, publishedAt, language, currency, source, sourceUrl,
            cancellationToken);

        return CreatedAtAction(nameof(GetFiling), new { id = response.Id }, response);
    }

    /// <summary>
    /// 查詢財報列表，可依股票、年度、類型篩選。
    /// </summary>
    /// <param name="securityId">選擇性 - 依股票識別碼篩選。</param>
    /// <param name="fiscalYear">選擇性 - 依年度篩選。</param>
    /// <param name="filingType">選擇性 - 依財報類型篩選。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinancialFilingSummaryResponse>>> ListFilings(
        [FromQuery] Guid? securityId = null,
        [FromQuery] int? fiscalYear = null,
        [FromQuery] string? filingType = null,
        CancellationToken cancellationToken = default)
    {
        var filings = await _filingService.ListAsync(securityId, fiscalYear, filingType, cancellationToken);
        return Ok(filings);
    }

    /// <summary>
    /// 依識別碼取得單筆財報 metadata。
    /// </summary>
    /// <param name="id">財報記錄識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FinancialFilingResponse>> GetFiling(Guid id, CancellationToken cancellationToken)
    {
        var filing = await _filingService.GetAsync(id, cancellationToken);
        return filing is null ? NotFound() : Ok(filing);
    }

    /// <summary>
    /// 刪除指定財報記錄（含 S3 檔案與資料庫記錄）。
    /// </summary>
    /// <param name="id">財報記錄識別碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFiling(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _filingService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
