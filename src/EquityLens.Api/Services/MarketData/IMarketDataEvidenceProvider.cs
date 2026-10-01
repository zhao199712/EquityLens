namespace EquityLens.Api.Services.MarketData;

public interface IMarketDataEvidenceProvider
{
    string? SourceProvenanceJson => null;
    string? LastPriceEvidenceJson { get; }
}
