namespace EquityLens.Api.Contracts.Risk;

public sealed record PortfolioRiskBacktestRunResponse(
    Guid Id,
    Guid PortfolioId,
    Guid JobId,
    string Status,
    int ProgressPercent,
    DateOnly From,
    DateOnly To,
    int LookbackDays,
    int Simulations,
    string AlgorithmVersion,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    PortfolioRiskBacktestResponse? Result);
