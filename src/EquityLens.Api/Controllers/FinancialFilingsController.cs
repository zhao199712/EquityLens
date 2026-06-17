using EquityLens.Api.Contracts.Filings;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.DocumentProcessing;
using EquityLens.Api.Services.FinancialFilings;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 財報檔案控制器，提供上市公司財報上傳、查詢、刪除與批次爬取功能。
/// </summary>
[Authorize]
[ApiController]
[Route("api/financial-filings")]
public sealed class FinancialFilingsController : ControllerBase
{
    private readonly IFinancialFilingService _filingService;
    private readonly ITwseFilingCrawler _crawler;
    private readonly IDocumentProcessingService _documentProcessing;
    private readonly ISecurityRepository _securityRepository;
    private readonly IObjectStorageService _objectStorage;
    private readonly ICurrentUserContext _currentUser;
    private readonly EquityLensDbContext _dbContext;

    /// <summary>
    /// 初始化財報檔案控制器。
    /// </summary>
    public FinancialFilingsController(
        IFinancialFilingService filingService,
        ITwseFilingCrawler crawler,
        IDocumentProcessingService documentProcessing,
        ISecurityRepository securityRepository,
        IObjectStorageService objectStorage,
        ICurrentUserContext currentUser,
        EquityLensDbContext dbContext)
    {
        _filingService = filingService;
        _crawler = crawler;
        _documentProcessing = documentProcessing;
        _securityRepository = securityRepository;
        _objectStorage = objectStorage;
        _currentUser = currentUser;
        _dbContext = dbContext;
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

    /// <summary>
    /// 批次爬取 TWSE 財報，下載 PDF、解析文字並存入資料庫。
    /// </summary>
    /// <param name="request">爬取請求，包含股票代號清單與年度區間。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>爬取結果摘要。</returns>
    [HttpPost("crawl")]
    public async Task<ActionResult<CrawlFilingsResponse>> CrawlFilings(
        [FromBody] CrawlFilingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StockCodes is null || request.StockCodes.Count == 0)
            return BadRequest(new { error = "stock_codes_required", message = "請提供至少一個股票代號。" });

        if (request.StartYear < 100 || request.EndYear > 120 || request.StartYear > request.EndYear)
            return BadRequest(new { error = "invalid_year_range", message = "年度區間無效。" });

        var userId = _currentUser.UserId;
        var failedFiles = new List<CrawlFilingsResponse.FailedItem>();
        var successCount = 0;

        foreach (var stockCode in request.StockCodes)
        {
            // 確認 security 是否存在，不存在則跳過
            var security = await _securityRepository.GetEntityByTickerExchangeAsync(
                stockCode, "TWSE", cancellationToken);

            for (var year = request.StartYear; year <= request.EndYear; year++)
            {
                try
                {
                    // 查詢可用財報
                    var filings = await _crawler.GetAvailableFilingsAsync(
                        stockCode, year, cancellationToken);

                    foreach (var filingInfo in filings)
                    {
                        try
                        {
                            // 下載 PDF
                            var pdfBytes = await _crawler.DownloadPdfAsync(filingInfo, cancellationToken);

                            // 存 PDF 到本地目錄
                            var localDir = Path.Combine("filings", "twse", stockCode, year.ToString());
                            Directory.CreateDirectory(localDir);
                            var localPath = Path.Combine(localDir, filingInfo.FileName);
                            await System.IO.File.WriteAllBytesAsync(localPath, pdfBytes, cancellationToken);

                            // 建立 UploadedFile 記錄（使用本地路徑）
                            var uploadedFile = new UploadedFile
                            {
                                Id = Guid.NewGuid(),
                                UploadedByUserId = userId,
                                BucketName = "local",
                                ObjectKey = localPath,
                                OriginalFileName = filingInfo.FileName,
                                ContentType = "application/pdf",
                                FileSizeBytes = pdfBytes.Length,
                                StorageProvider = "Local",
                                UploadStatus = "Uploaded",
                                CreatedAtUtc = DateTime.UtcNow
                            };

                            _dbContext.UploadedFiles.Add(uploadedFile);

                            // 建立 FinancialFiling 記錄
                            var filing = new FinancialFiling
                            {
                                Id = Guid.NewGuid(),
                                SecurityId = security?.Id ?? Guid.Empty,
                                UploadedFileId = uploadedFile.Id,
                                UploadedByUserId = userId,
                                FilingType = "QuarterlyReport",
                                FiscalYear = year,
                                FiscalQuarter = ParseQuarter(filingInfo.Quarter),
                                Language = "zh",
                                Currency = "TWD",
                                Source = "TWSE",
                                SourceUrl = $"https://doc.twse.com.tw/server-java/t57sb01?co_id={stockCode}&year={year}&mtype=A",
                                ParseStatus = "Pending",
                                CreatedAtUtc = DateTime.UtcNow
                            };

                            _dbContext.FinancialFilings.Add(filing);
                            await _dbContext.SaveChangesAsync(cancellationToken);

                            // 解析 PDF 並建立 Document + DocumentChunk 記錄
                            var result = await _documentProcessing.ProcessFilingAsync(
                                filing.Id, cancellationToken);

                            if (result.Success)
                                successCount++;
                            else
                                failedFiles.Add(new CrawlFilingsResponse.FailedItem(
                                    stockCode, year, result.ErrorMessage ?? "解析失敗"));
                        }
                        catch (Exception ex)
                        {
                            failedFiles.Add(new CrawlFilingsResponse.FailedItem(
                                stockCode, year, ex.Message));
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedFiles.Add(new CrawlFilingsResponse.FailedItem(
                        stockCode, year, $"查詢失敗: {ex.Message}"));
                }
            }
        }

        var totalRequested = request.StockCodes.Count * (request.EndYear - request.StartYear + 1);
        return Ok(new CrawlFilingsResponse(
            totalRequested, successCount, failedFiles.Count, failedFiles));
    }

    private static int? ParseQuarter(string quarter)
    {
        return quarter switch
        {
            "Q1" => 1,
            "Q2" => 2,
            "Q3" => 3,
            "Q4" => 4,
            _ => null
        };
    }
}
