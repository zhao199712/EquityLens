namespace EquityLens.Api.Data.Entities;

public class PortfolioHolding
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid SecurityId { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public string CostCurrency { get; set; } = "USD";
    public string? Note { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Portfolio Portfolio { get; set; } = null!;
    public Security Security { get; set; } = null!;
}
