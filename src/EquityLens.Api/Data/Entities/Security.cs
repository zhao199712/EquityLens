namespace EquityLens.Api.Data.Entities;

public class Security
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AssetType { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Isin { get; set; }
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? MetadataUpdatedAtUtc { get; set; }
    public string? MetadataSource { get; set; }
    public DateTime? PricesSyncedAtUtc { get; set; }
    public string? PricesSource { get; set; }

    // Navigation
    public ICollection<PortfolioHolding> Holdings { get; set; } = [];
    public ICollection<MarketPrice> Prices { get; set; } = [];
    public ICollection<FinancialStatement> FinancialStatements { get; set; } = [];
}
