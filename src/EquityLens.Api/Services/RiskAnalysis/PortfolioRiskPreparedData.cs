using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.RiskAnalysis;

/// <summary>正式風險計算使用的估值、共同日期與報酬資料。</summary>
public sealed record PortfolioRiskPreparedData(
    PortfolioDetailResponse Portfolio,
    IReadOnlyList<PortfolioHoldingResponse> PricedHoldings,
    IReadOnlyList<(PortfolioHoldingResponse Holding, decimal MarketValueInBase)> MarketValues,
    decimal TotalMarketValue,
    IReadOnlyList<DateOnly> CommonDates,
    IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> AlignedPrices,
    IReadOnlyDictionary<Guid, decimal> Weights,
    IReadOnlyList<decimal> PortfolioReturns);

/// <summary>年度模擬使用的價格、權重與估值。</summary>
public sealed record PortfolioSimulationData(
    IReadOnlyDictionary<Guid, IReadOnlyList<decimal>> AlignedPrices,
    IReadOnlyDictionary<Guid, decimal> Weights,
    decimal TotalMarketValue);

/// <summary>年度模擬的資料準備結果，保留資料不足時的成功回應語意。</summary>
public sealed record PortfolioMonteCarloPreparedData(
    PortfolioSimulationData? Data,
    string? InsufficientMessage,
    DateOnly? DataAsOf,
    int CommonTradingDays);
