namespace EquityLens.Api.Data.Entities;

public class ScenarioResult
{
    public Guid Id { get; set; }
    public Guid RiskRunId { get; set; }
    public Guid ScenarioId { get; set; }
    public decimal PortfolioValueBefore { get; set; }
    public decimal PortfolioValueAfter { get; set; }
    public decimal PnlAmount { get; set; }
    public decimal PnlPercent { get; set; }

    // Navigation
    public RiskRun RiskRun { get; set; } = null!;
    public Scenario Scenario { get; set; } = null!;
}
