using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.CurrentUser;

/// <summary>對外摘要不含原始回答；模型不可推測或寫入偏好。</summary>
public sealed record RiskPreferenceProfile(string Value, string Label, bool IsDefault, string Statement,
    string Source = "Default", string? CapacityLevel = null, string? WillingnessLevel = null,
    DateTime? AssessedAt = null, bool Stale = false, string? QuestionnaireVersion = null, string ThresholdVersion = "v1")
{
    public string Level => Label;
    public static bool IsValid(string? value) => value is "Conservative" or "Balanced" or "Aggressive";
    public static string LabelFor(string value) => value switch { "Conservative" => "保守", "Aggressive" => "積極", _ => "穩健" };
    public static RiskPreferenceProfile FromSaved(string? saved) => FromProfile(null, saved, DateTime.UtcNow);
    public static RiskPreferenceProfile FromProfile(UserRiskProfile? profile, string? legacyManual, DateTime now)
    {
        var value = profile?.Level ?? (IsValid(legacyManual) ? legacyManual! : "Balanced");
        var isDefault = profile is null && !IsValid(legacyManual);
        var label = LabelFor(value);
        var stale = profile?.AssessedAtUtc is { } at && now > at.AddMonths(12);
        var source = profile?.Source ?? (isDefault ? "Default" : "Manual");
        var statement = isDefault ? $"依預設的風險偏好『{label}』（尚未評估，可在設定中完成評估）"
            : $"依你的風險偏好『{label}』（{(source == "Manual" ? "手動選擇" : "問卷評估")}，可在設定修改）";
        if (profile?.CapacityLevel is { } c && profile.WillingnessLevel is { } w)
            statement += $"；承受能力『{LabelFor(c)}』、承受意願『{LabelFor(w)}』，採較低等級";
        if (stale) statement += "；此評估可能過時，請在設定中重新評估";
        return new(value, label, isDefault, statement, source, profile?.CapacityLevel, profile?.WillingnessLevel,
            profile?.AssessedAtUtc, stale, profile?.QuestionnaireVersion, profile?.ThresholdVersion ?? "v1");
    }
}
