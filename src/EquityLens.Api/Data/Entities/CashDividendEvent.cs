namespace EquityLens.Api.Data.Entities;

public class CashDividendEvent
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public DateOnly ExDividendDate { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public decimal CashAmountPerShare { get; set; }
    public string Currency { get; set; } = "TWD";
    public string Source { get; set; } = "FinMind";
    public string SourceKey { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Security Security { get; set; } = null!;
}
