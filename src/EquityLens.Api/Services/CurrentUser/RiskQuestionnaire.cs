namespace EquityLens.Api.Services.CurrentUser;

/// <summary>EquityLens 自訂六題評估 v1；不是 Grable–Lytton 量表。0 只用於 W3 未經歷。</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class RiskQuestionnaireAnswers
{
    public int C1 { get; init; }
    public int C2 { get; init; }
    public int C3 { get; init; }
    public int W1 { get; init; }
    public int W2 { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public int W3 { get; init; }
    // 不使用 record 的自動 ToString，避免意外記錄財務回答。
    public override string ToString() => nameof(RiskQuestionnaireAnswers);
}

public sealed record RiskQuestionnaireResult(int CapacityScore, int WillingnessScore, string CapacityLevel, string WillingnessLevel, string Level);

public static class RiskQuestionnaire
{
    public const string Version = "equitylens-v1";
    public static bool IsValid(RiskQuestionnaireAnswers a) => new[] { a.C1, a.C2, a.C3, a.W1, a.W2 }.All(v => v is >= 1 and <= 3) && a.W3 is >= 0 and <= 3;
    public static string Level(int score) => score switch { >= 3 and <= 5 => "Conservative", 6 or 7 => "Balanced", 8 or 9 => "Aggressive", _ => throw new ArgumentOutOfRangeException(nameof(score)) };
    public static RiskQuestionnaireResult Score(RiskQuestionnaireAnswers a)
    {
        if (!IsValid(a)) throw new ArgumentException("Invalid questionnaire options.");
        var capacity = a.C1 + a.C2 + a.C3;
        var willingness = (int)decimal.Round((a.W1 + a.W2 + a.W3) * 3m / (a.W3 == 0 ? 2 : 3), 0, MidpointRounding.AwayFromZero);
        var capacityLevel = a.C1 == 1 || a.C2 == 1 ? "Conservative" : Level(capacity);
        var willingnessLevel = Level(willingness);
        string[] levels = ["Conservative", "Balanced", "Aggressive"];
        return new(capacity, willingness, capacityLevel, willingnessLevel, levels[Math.Min(Array.IndexOf(levels, capacityLevel), Array.IndexOf(levels, willingnessLevel))]);
    }
}
