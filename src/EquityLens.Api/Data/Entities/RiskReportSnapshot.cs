namespace EquityLens.Api.Data.Entities;

public sealed class RiskReportSnapshot
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateOnly? DataAsOfDate { get; set; }
    public string Model { get; set; } = "MVEWMA-FHS";
    public string ThresholdVersion { get; set; } = "balanced-v1";
    public string SnapshotJson { get; set; } = "{}";
    public Portfolio Portfolio { get; set; } = null!;
}
