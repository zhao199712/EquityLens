using EquityLens.Api.Common;
using EquityLens.Api.Data;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

/// <summary>目前使用者的實際偏好設定；不可指定他人的使用者 ID。</summary>
[Authorize]
[ApiController]
[Route("api/settings")]
public sealed class SettingsController(EquityLensDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    public sealed record UpdateRiskPreferenceRequest(string Value);

    /// <summary>讀取風險偏好，未設定時明確回傳穩健預設。</summary>
    [HttpGet("risk-preference")]
    public async Task<ActionResult<RiskPreferenceProfile>> GetRiskPreference(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.UserId && x.IsActive, ct);
        return user is null ? NotFound() : Ok(RiskPreferenceProfile.FromSaved(user.RiskPreference));
    }

    /// <summary>保存保守、穩健或積極偏好；明確選擇穩健後不再標示為預設。</summary>
    [HttpPut("risk-preference")]
    public async Task<ActionResult<RiskPreferenceProfile>> UpdateRiskPreference(UpdateRiskPreferenceRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        if (!RiskPreferenceProfile.IsValid(request.Value)) return BadRequest(new ApiError("risk_preference_invalid", "請選擇保守、穩健或積極。"));
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == currentUser.UserId && x.IsActive, ct);
        if (user is null) return NotFound();
        user.RiskPreference = request.Value;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(RiskPreferenceProfile.FromSaved(user.RiskPreference));
    }
}
