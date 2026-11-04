namespace EquityLens.Api.Services.CurrentUser;

/// <summary>風險偏好為使用者設定或明示的預設，不代表適合度評估或量化承受門檻。</summary>
public sealed record RiskPreferenceProfile(string Value, string Label, bool IsDefault, string Statement)
{
    public static bool IsValid(string? value) => value is "Conservative" or "Balanced" or "Aggressive";
    public static RiskPreferenceProfile FromSaved(string? saved)
    {
        var isDefault = !IsValid(saved);
        var value = isDefault ? "Balanced" : saved!;
        var label = value switch { "Conservative" => "保守", "Aggressive" => "積極", _ => "穩健" };
        return new(value, label, isDefault, $"依你的風險偏好『{label}』" + (isDefault ? "（預設，可在設定修改）" : "（已保存，可在設定修改）"));
    }
}
