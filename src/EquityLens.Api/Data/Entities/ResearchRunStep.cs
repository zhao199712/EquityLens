namespace EquityLens.Api.Data.Entities;

public class ResearchRunStep
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string StepType { get; set; } = string.Empty;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public long? DurationMs { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public ResearchRun Run { get; set; } = null!;
}
