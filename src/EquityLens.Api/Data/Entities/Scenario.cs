namespace EquityLens.Api.Data.Entities;

public class Scenario
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ShockType { get; set; } = string.Empty; // Percentage, Absolute
    public decimal ShockValue { get; set; }
    public string TargetType { get; set; } = string.Empty; // Market, Sector, Security
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<ScenarioResult> ScenarioResults { get; set; } = [];
}
