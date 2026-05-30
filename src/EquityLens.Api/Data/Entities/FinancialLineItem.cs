namespace EquityLens.Api.Data.Entities;

public class FinancialLineItem
{
    public Guid Id { get; set; }
    public Guid FinancialStatementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Unit { get; set; }

    // Navigation
    public FinancialStatement FinancialStatement { get; set; } = null!;
}
