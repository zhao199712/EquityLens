using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.FinancialData;
using EquityLens.Api.Services.InvestorConferences;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.Redis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 管理後台資料匯入工作控制器，將耗時的匯入作業放入 Redis 背景佇列執行。
/// </summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/jobs")]
public sealed class AdminJobsController : ControllerBase
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IBackgroundJobQueue _queue;
    private readonly ICurrentUserContext _currentUser;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public AdminJobsController(
        EquityLensDbContext dbContext,
        IBackgroundJobQueue queue,
        ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _queue = queue;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 查詢資料匯入工作列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobRunSummaryResponse>>> List(
        [FromQuery] int limit = 50,
        [FromQuery] string? jobType = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.JobRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(jobType))
            query = query.Where(j => j.JobType == jobType);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(j => j.Status == status);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(jobs.Select(ToSummaryResponse).ToList());
    }

    /// <summary>
    /// 取得單筆資料匯入工作詳情。
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobRunDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.JobRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null) return NotFound();

        return Ok(ToDetailResponse(job));
    }

    /// <summary>
    /// 建立新的資料匯入工作並加入 Redis 背景佇列。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<JobRunSummaryResponse>> Create(
        [FromBody] CreateImportJobRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobType))
            return BadRequest(new { error = "job_type_required", message = "請指定 jobType。" });

        if (!IsKnownJobType(request.JobType))
            return BadRequest(new { error = "unknown_job_type", message = $"不支援的 jobType: {request.JobType}" });

        var payload = BuildPayload(request);
        var jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = _currentUser.UserId,
            JobType = request.JobType,
            Status = "Queued",
            ProgressPercent = 0,
            PayloadJson = payload,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _dbContext.JobRuns.Add(jobRun);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var backgroundJob = new BackgroundJob(
            jobRun.Id,
            request.JobType,
            jobRun.Id.ToString(),
            payload,
            jobRun.CreatedAtUtc);

        var streamId = await _queue.EnqueueAsync(backgroundJob, cancellationToken);

        jobRun.RedisJobId = streamId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToSummaryResponse(jobRun));
    }

    /// <summary>
    /// 建立元大台灣50 ETF（0050）現行股票成分的批次價格同步工作。
    /// 由背景工作逐檔補缺，呼叫端可透過既有 jobs API 輪詢進度與結果。
    /// </summary>
    [HttpPost("sync-tw0050-prices")]
    public Task<ActionResult<JobRunSummaryResponse>> SyncTw0050Prices(CancellationToken cancellationToken)
    {
        return Create(new CreateImportJobRequest("SyncTw0050Prices"), cancellationToken);
    }

    /// <summary>
    /// 取消尚未執行或執行中的資料匯入工作。
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<JobRunSummaryResponse>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.JobRuns.FindAsync(id, cancellationToken);
        if (job is null) return NotFound();

        if (job.Status is "Completed" or "Failed" or "Cancelled")
            return BadRequest(new { error = "job_already_terminal", message = "此工作已處於終止狀態。" });

        job.Status = "Cancelled";
        job.CompletedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToSummaryResponse(job));
    }

    private static string BuildPayload(CreateImportJobRequest request)
    {
        var dict = new Dictionary<string, object?>();

        if (request.From.HasValue)
            dict["from"] = request.From.Value.ToString("yyyy-MM-dd");

        if (request.To.HasValue)
            dict["to"] = request.To.Value.ToString("yyyy-MM-dd");

        if (!string.IsNullOrWhiteSpace(request.Ticker))
            dict["ticker"] = request.Ticker;

        if (!string.IsNullOrWhiteSpace(request.Exchange))
            dict["exchange"] = request.Exchange;

        return JsonSerializer.Serialize(dict, SerializerOptions);
    }

    private static bool IsKnownJobType(string jobType) => jobType switch
    {
        "ImportConferences" => true,
        "ChunkConferences" => true,
        "EmbedChunks" => true,
        "ImportFinMindFinancials" => true,
        "ImportFinMindDividends" => true,
        "ImportMopsFinancials" => true,
        "ImportTwseReportFiles" => true,
        "ImportMarketPrices" => true,
        "SyncTw0050Prices" => true,
        _ => false,
    };

    private static JobRunSummaryResponse ToSummaryResponse(JobRun job) =>
        new(job.Id, job.JobType, job.Status, job.ProgressPercent, job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc, job.ErrorMessage);

    private static JobRunDetailResponse ToDetailResponse(JobRun job) =>
        new(job.Id, job.JobType, job.Status, job.ProgressPercent, job.PayloadJson, job.ResultJson, job.ErrorMessage, job.RedisJobId, job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc);
}

public sealed record CreateImportJobRequest(
    string JobType,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Ticker = null,
    string? Exchange = null);

public sealed record JobRunSummaryResponse(
    Guid Id,
    string JobType,
    string Status,
    int ProgressPercent,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

public sealed record JobRunDetailResponse(
    Guid Id,
    string JobType,
    string Status,
    int ProgressPercent,
    string? PayloadJson,
    string? ResultJson,
    string? ErrorMessage,
    string? RedisJobId,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);
