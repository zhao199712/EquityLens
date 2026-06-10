namespace EquityLens.Api.Data.Entities;

public class ExchangeRate
{
    public Guid Id { get; set; }
    public string SourceCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
