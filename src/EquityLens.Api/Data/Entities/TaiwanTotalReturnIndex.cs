namespace EquityLens.Api.Data.Entities;

public sealed class TaiwanTotalReturnIndex
{
    public Guid Id { get; set; }
    public DateOnly TradingDate { get; set; }
    public decimal Value { get; set; }
    public string Source { get; set; } = "FinMind";
    public DateTime UpdatedAtUtc { get; set; }
}
