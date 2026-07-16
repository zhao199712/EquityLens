namespace EquityLens.Api.Contracts.Risk;

public sealed record PortfolioRiskBacktestResponse(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    int LookbackDays,
    int ObservationCount,
    IReadOnlyList<PortfolioRiskBacktestModelResponse> Models);

public sealed record PortfolioRiskBacktestModelResponse(
    string Model,
    decimal ConfidenceLevel,
    int ObservationCount,
    int BreachCount,
    decimal BreachRate,
    decimal ExpectedBreachRate,
    decimal? KupiecPValue,
    decimal? ChristoffersenPValue,
    int TailObservationCount,
    decimal? ActualTailLossAverage,
    decimal? PredictedEsAverage,
    decimal? EsTailLossRatio,
    string EsStatus,
    string Status,
    IReadOnlyList<PortfolioRiskBacktestPoint> Points);

public sealed record PortfolioRiskBacktestPoint(
    DateOnly Date,
    decimal ActualReturn,
    decimal PredictedVaR,
    decimal PredictedES,
    bool Breached);
