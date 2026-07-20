namespace EquityLens.Api.Contracts.Risk;
public sealed record PortfolioRiskGovernanceResponse(Guid PortfolioId, string DataStatus, DateOnly? DataAsOfDate, int CommonTradingDays, IReadOnlyList<PortfolioRiskAlertResponse> Alerts);
public sealed record PortfolioRiskAlertResponse(string Code, string Status, decimal CurrentValue, decimal WarningThreshold, decimal CriticalThreshold, string Message);
