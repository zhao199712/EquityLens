using System.Security.Claims;
using EquityLens.Api.Common;
using EquityLens.Api.Data;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Controllers.Filters;

/// <summary>
/// 每位使用者同時進行中的 agent run 上限（設定區段 <c>RateLimiting:ActiveAgentRuns</c>）。
/// </summary>
public sealed class ActiveAgentRunLimitOptions
{
    public const string SectionName = "RateLimiting:ActiveAgentRuns";

    /// <summary>同時處於 Pending／Running 的 run 上限。</summary>
    public int MaxActiveRunsPerUser { get; set; } = 2;

    /// <summary>
    /// 只計算這段時間內建立的 run。worker 崩潰時 run 可能一直停在 Running，
    /// 這個時間窗避免使用者因為卡住的 run 被永久擋住。
    /// </summary>
    public int ActiveWindowMinutes { get; set; } = 30;
}

/// <summary>
/// 限制建立 agent run 的端點：一次 HTTP 請求背後可能展開成多次 LLM 呼叫，
/// 所以除了每分鐘請求數之外，也要限制同時執行中的 run 數量。管理員不受此限制。
/// </summary>
public sealed class LimitActiveAgentRunsAttribute : TypeFilterAttribute
{
    public LimitActiveAgentRunsAttribute() : base(typeof(ActiveAgentRunLimitFilter))
    {
    }
}

internal sealed class ActiveAgentRunLimitFilter(EquityLensDbContext dbContext, IOptions<ActiveAgentRunLimitOptions> options)
    : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.IsInRole("Admin") || !Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            // 管理員不限；未登入的請求交給 [Authorize] 處理。
            await next();
            return;
        }

        var limit = options.Value;
        var since = DateTime.UtcNow.AddMinutes(-Math.Max(1, limit.ActiveWindowMinutes));
        var active = await dbContext.AgentRuns.CountAsync(
            run => run.UserId == userId
                && (run.Status == AgentRunStatuses.Pending || run.Status == AgentRunStatuses.Running)
                && run.CreatedAtUtc >= since,
            context.HttpContext.RequestAborted);

        if (active >= Math.Max(1, limit.MaxActiveRunsPerUser))
        {
            context.Result = new ObjectResult(new ApiError(
                "active_run_limit",
                $"目前已有 {active} 個分析正在執行，請等待完成後再建立新的分析。"))
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
            return;
        }

        await next();
    }
}
