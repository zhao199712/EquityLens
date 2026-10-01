namespace EquityLens.Api.Data.Entities;

/// <summary>不可變的調整批次；舊價格的空白批次代表尚未驗證。</summary>
public sealed class PriceAdjustmentBatch
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateOnly VerifiedThrough { get; set; }
    public string Source { get; set; } = "";
    public string AlgorithmVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public string SeriesHash { get; set; } = "";
}
