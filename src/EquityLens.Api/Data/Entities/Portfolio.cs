namespace EquityLens.Api.Data.Entities;

public class Portfolio
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public AppUser Owner { get; set; } = null!;
    public ICollection<PortfolioHolding> Holdings { get; set; } = [];
    public ICollection<PortfolioSnapshot> Snapshots { get; set; } = [];
    public ICollection<RiskRun> RiskRuns { get; set; } = [];
    public ICollection<FinancialReport> FinancialReports { get; set; } = [];
}
