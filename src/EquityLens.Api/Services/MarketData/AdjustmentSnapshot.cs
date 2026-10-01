using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.MarketData;

public sealed record AdjustmentSnapshot(
    IReadOnlyDictionary<DateOnly, decimal> AdjustedCloses,
    IReadOnlyList<CorporatePriceAction> Actions,
    string EvidenceJson,
    DateTime FetchedAtUtc,
    bool ActionsComplete);

/// <summary>官方合併因子優先，同日不可重複套用。</summary>
public sealed record CorporatePriceAction(DateOnly Date, string Kind, decimal Value, decimal? OfficialFactor = null);

public interface IAdjustedCloseProvider
{
    Task<AdjustmentSnapshot> GetAdjustmentSnapshotAsync(Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record TaiwanPriceEvidence(
    IReadOnlySet<DateOnly> TradingDates,
    IReadOnlyList<CorporatePriceAction> Actions,
    IReadOnlySet<DateOnly> ConfirmedNoTradeDates,
    IReadOnlyDictionary<DateOnly, ImportedMarketPrice> Corrections,
    string EvidenceJson,
    bool ActionsComplete);

public interface ITaiwanPriceEvidenceProvider
{
    Task<TaiwanPriceEvidence> GetEvidenceAsync(Security security, DateOnly from, DateOnly to,
        IReadOnlyList<ImportedMarketPrice> prices, bool requireActions, CancellationToken cancellationToken);
}
