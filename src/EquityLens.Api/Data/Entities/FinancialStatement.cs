namespace EquityLens.Api.Data.Entities;

public class FinancialStatement
{
    public Guid Id { get; set; }
    public Guid SecurityId { get; set; }
    public string StatementType { get; set; } = string.Empty; // IncomeStatement, BalanceSheet, CashFlow
    public string PeriodType { get; set; } = string.Empty; // Annual, Quarterly
    public int FiscalYear { get; set; }
    public int? FiscalQuarter { get; set; }
    public DateOnly PeriodEndDate { get; set; }
    public DateOnly? PublishedDate { get; set; }
    public string Currency { get; set; } = "USD";
    public string? DataSource { get; set; }

    // Navigation
    public Security Security { get; set; } = null!;
    public ICollection<FinancialLineItem> LineItems { get; set; } = [];
}
