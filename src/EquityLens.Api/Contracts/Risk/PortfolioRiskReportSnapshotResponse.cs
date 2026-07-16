using System.Text.Json;

namespace EquityLens.Api.Contracts.Risk;

/// <summary>不可變風險報告快照的清單項目。</summary>
public sealed record PortfolioRiskReportSnapshotListItemResponse(
    Guid Id,
    DateTime CreatedAtUtc,
    DateOnly? DataAsOfDate,
    string Model,
    string ThresholdVersion,
    string OverallStatus);

/// <summary>不可變風險報告快照；Snapshot 僅含報酬率與風險判定，不含幣別或金額。</summary>
public sealed record PortfolioRiskReportSnapshotDetailResponse(
    Guid Id,
    Guid PortfolioId,
    DateTime CreatedAtUtc,
    DateOnly? DataAsOfDate,
    string Model,
    string ThresholdVersion,
    string OverallStatus,
    JsonElement Snapshot);
