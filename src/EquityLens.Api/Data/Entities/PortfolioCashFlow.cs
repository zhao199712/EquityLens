namespace EquityLens.Api.Data.Entities;

public class PortfolioCashFlow
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid? SecurityId { get; set; }
    public Guid? CashDividendEventId { get; set; }
    public string FlowType { get; set; } = string.Empty; // Deposit, Withdrawal, Dividend, DividendTax, Fee
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TWD";
    public DateOnly EffectiveDate { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Confirmed, Skipped
    public bool IsUserAdjusted { get; set; }
    public bool IsSystemDerived { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Portfolio Portfolio { get; set; } = null!;
    public Security? Security { get; set; }
    public CashDividendEvent? CashDividendEvent { get; set; }
}
