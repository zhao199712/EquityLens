namespace EquityLens.Api.Data.Entities;

public class PortfolioSnapshot
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public decimal MarketValue { get; set; }
    public decimal CostValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal? DailyReturn { get; set; }
    public string Currency { get; set; } = "USD";

    // Navigation
    public Portfolio Portfolio { get; set; } = null!;
}
