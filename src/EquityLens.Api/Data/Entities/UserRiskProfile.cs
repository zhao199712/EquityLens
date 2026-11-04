using System.Text.Json.Serialization;

namespace EquityLens.Api.Data.Entities;

/// <summary>本人目前有效的風險評估。原始回答不得序列化至工具、紀錄或追蹤。</summary>
public sealed class UserRiskProfile
{
    public Guid UserId { get; set; }
    public string Level { get; set; } = "Balanced";
    public string Source { get; set; } = "Manual";
    public string? CapacityLevel { get; set; }
    public string? WillingnessLevel { get; set; }
    public int? CapacityScore { get; set; }
    public int? WillingnessScore { get; set; }
    public string? QuestionnaireVersion { get; set; }
    public string ThresholdVersion { get; set; } = "v1";
    [JsonIgnore]
    public string? AnswersJson { get; set; }
    public DateTime? AssessedAtUtc { get; set; }
}
