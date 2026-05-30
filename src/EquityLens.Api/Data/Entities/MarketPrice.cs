namespace EquityLens.Api.Data.Entities;

public class MarketPrice
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public DateTime PriceTime { get; set; }
    public string Interval { get; set; } = "1d";
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal? AdjustedClose { get; set; }
    public long? Volume { get; set; }
    public string? DataSource { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Security Security { get; set; } = null!;
}
