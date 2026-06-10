namespace EquityLens.Api.Data.Entities;

public class PortfolioTransaction
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid SecurityId { get; set; }
    public string TransactionType { get; set; } = string.Empty; // "BUY" / "SELL"
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Portfolio Portfolio { get; set; } = null!;
    public Security Security { get; set; } = null!;
}
