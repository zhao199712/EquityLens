namespace EquityLens.Api.Services.CurrentUser;

/// <summary>產品負責人已確認的參考門檻。不是問卷研究推導出的投資適合度標準。</summary>
public static class RiskProfileThresholds
{
    // 依據：2026-11-04 使用者 P3 補充規格的示意值；核對日 2026-11-04。
    // 使用者於 2026-11-04 本串明確確認採用；變更數值必須新增版本並經產品確認。
    public static RiskThresholdDefinition V1 { get; } = new("v1", true);
}

public sealed record RiskThresholdValues(string Version, decimal AnnualVolatilityMax, decimal MaxDrawdownFloor);

public sealed class RiskThresholdDefinition
{
    public string Version { get; }
    public bool Approved { get; }
    internal RiskThresholdDefinition(string version, bool approved) { Version = version; Approved = approved; }
    internal RiskThresholdValues For(string level) => level switch
    {
        "Conservative" => new(Version, .10m, -.15m),
        "Aggressive" => new(Version, .30m, -.40m),
        "Balanced" => new(Version, .18m, -.25m),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
