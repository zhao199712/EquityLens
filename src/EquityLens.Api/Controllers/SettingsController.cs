using System.Text.Json;
using System.Text.Json.Serialization;
using EquityLens.Api.Common;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
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
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record UpdateRiskPreferenceRequest(string Value);

    private Task<AppUser?> Owner(CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Id == currentUser.UserId && x.IsActive, ct);
    private Task<UserRiskProfile?> Profile(CancellationToken ct) => db.UserRiskProfiles.SingleOrDefaultAsync(x => x.UserId == currentUser.UserId, ct);
    private static RiskPreferenceProfile Summary(UserRiskProfile? profile, AppUser user) => RiskPreferenceProfile.FromProfile(profile, user.RiskPreference, DateTime.UtcNow);

    /// <summary>只讀取本人摘要，未設定時明確回傳穩健預設，不回傳原始問卷。</summary>
    [HttpGet("risk-preference")]
    public async Task<ActionResult<RiskPreferenceProfile>> GetRiskPreference(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        var user = await Owner(ct);
        return user is null ? NotFound() : Ok(Summary(await Profile(ct), user));
    }

    /// <summary>手動選擇會取代問卷並刪除回答與子等級，來源標示 Manual。</summary>
    [HttpPut("risk-preference")]
    public async Task<ActionResult<RiskPreferenceProfile>> UpdateRiskPreference(UpdateRiskPreferenceRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        if (!RiskPreferenceProfile.IsValid(request.Value)) return BadRequest(new ApiError("risk_preference_invalid", "請選擇保守、穩健或積極。"));
        var user = await Owner(ct);
        if (user is null) return NotFound();
        var profile = await Profile(ct) ?? AddProfile();
        profile.Level = request.Value;
        profile.Source = "Manual";
        profile.CapacityLevel = profile.WillingnessLevel = profile.AnswersJson = profile.QuestionnaireVersion = null;
        profile.CapacityScore = profile.WillingnessScore = null;
        profile.ThresholdVersion = RiskProfileThresholds.V1.Version;
        profile.AssessedAtUtc = DateTime.UtcNow;
        user.RiskPreference = request.Value; // 舊版手動欄位相容；刪除與問卷提交均清空。
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Summary(profile, user));
    }

    /// <summary>本人提交自訂六題評估；計分與等級只由程式決定。</summary>
    [HttpPost("risk-preference/questionnaire")]
    public async Task<ActionResult<RiskPreferenceProfile>> SubmitQuestionnaire(RiskQuestionnaireAnswers answers, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        if (!RiskQuestionnaire.IsValid(answers)) return BadRequest(new ApiError("risk_questionnaire_invalid", "請完成所有題目並選擇有效選項。"));
        var user = await Owner(ct);
        if (user is null) return NotFound();
        var score = RiskQuestionnaire.Score(answers);
        var profile = await Profile(ct) ?? AddProfile();
        profile.Level = score.Level;
        profile.Source = "Questionnaire";
        profile.CapacityLevel = score.CapacityLevel;
        profile.WillingnessLevel = score.WillingnessLevel;
        profile.CapacityScore = score.CapacityScore;
        profile.WillingnessScore = score.WillingnessScore;
        profile.QuestionnaireVersion = RiskQuestionnaire.Version;
        profile.ThresholdVersion = RiskProfileThresholds.V1.Version;
        profile.AnswersJson = JsonSerializer.Serialize(answers);
        profile.AssessedAtUtc = DateTime.UtcNow;
        user.RiskPreference = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Summary(profile, user));
    }

    /// <summary>真正刪除目前評估與回答，清空舊手動欄位，回到明示預設。</summary>
    [HttpDelete("risk-preference")]
    public async Task<ActionResult<RiskPreferenceProfile>> DeleteRiskPreference(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Unauthorized();
        var user = await Owner(ct);
        if (user is null) return NotFound();
        if (await Profile(ct) is { } profile) db.UserRiskProfiles.Remove(profile);
        user.RiskPreference = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Summary(null, user));
    }

    private UserRiskProfile AddProfile()
    {
        var profile = new UserRiskProfile { UserId = currentUser.UserId };
        db.UserRiskProfiles.Add(profile);
        return profile;
    }
}
