namespace EquityLens.Api.Contracts.Risk;

/// <summary>正式一年期 MVEWMA-FHS 蒙地卡羅路徑結果。</summary>
public sealed record PortfolioMonteCarloResponse(
    Guid PortfolioId,
    string Status,
    string? Message,
    DateOnly? DataAsOfDate,
    int CommonTradingDays,
    int HorizonDays,
    int Simulations,
    string Model,
    decimal EwmaLambda,
    decimal ShrinkageAlpha,
    decimal ResidualCapQuantile,
    decimal CappedDrawRate,
    IReadOnlyList<PortfolioMonteCarloBandPoint> Bands,
    IReadOnlyList<PortfolioMonteCarloPath> SamplePaths,
    decimal PositiveReturnProbability,
    decimal ExpectedReturn,
    decimal P5FinalReturn,
    decimal P1FinalReturn,
    PortfolioMonteCarloDiagnostics Diagnostics);

/// <summary>某一模擬日的累積報酬分位數，所有數值皆為報酬率。</summary>
public sealed record PortfolioMonteCarloBandPoint(
    int Day,
    decimal P1,
    decimal P5,
    decimal P50,
    decimal P95,
    decimal P99);

/// <summary>供圖表顯示的代表性模擬路徑。</summary>
public sealed record PortfolioMonteCarloPath(int PathIndex, IReadOnlyList<decimal> CumulativeReturns);

/// <summary>模型輸入與期末路徑分布的可解釋性診斷；所有數值皆非幣別。</summary>
public sealed record PortfolioMonteCarloDiagnostics(
    decimal AnnualizedPortfolioVolatility,
    decimal ResidualNormP99,
    decimal MaxResidualNorm,
    decimal P50FinalReturn,
    decimal P95FinalReturn,
    decimal P99FinalReturn,
    decimal ExpectedMedianGap,
    decimal ResidualCapQuantile,
    decimal CappedDrawRate,
    bool RightSkewWarning,
    string? RightSkewMessage);
