namespace EquityLens.Api.Contracts.Risk;

public sealed record PortfolioStressTestResponse(Guid PortfolioId, DateOnly? DataAsOfDate, IReadOnlyList<PortfolioStressScenarioResponse> Scenarios);
public sealed record PortfolioStressScenarioResponse(string Id, string Name, string Type, string Status, string Methodology, DateOnly? From, DateOnly? To, decimal TotalImpact, IReadOnlyList<PortfolioStressHoldingResponse> Holdings, IReadOnlyList<PortfolioStressIndustryResponse> Industries);
public sealed record PortfolioStressHoldingResponse(string Ticker, string SecurityName, string Industry, decimal Weight, decimal BasePrice, decimal StressedPrice, decimal Shock, decimal Contribution);
public sealed record PortfolioStressIndustryResponse(string Industry, decimal Weight, decimal Impact, decimal Contribution);
