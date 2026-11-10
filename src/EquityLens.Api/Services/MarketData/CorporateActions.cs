namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// 官方公司行動（除權息、分割、減資）。參考價為事件後的理論價格，前一日收盤價供核對資料一致性。
/// </summary>
/// <param name="Date">事件生效日（恢復交易首日）。</param>
/// <param name="Kind">事件種類，例如 dividend:權息、split:分割、capital_reduction。</param>
/// <param name="ReferencePrice">事件後參考價。</param>
/// <param name="BeforePrice">事件前一個交易日收盤價。</param>
public sealed record CorporateAction(DateOnly Date, string Kind, decimal ReferencePrice, decimal BeforePrice);

/// <summary>
/// 提供官方公司行動資料，用於以原始收盤價重建全歷史還原價。
/// </summary>
public interface ICorporateActionProvider
{
    /// <summary>
    /// 取得指定期間的公司行動，依日期升冪排序。
    /// </summary>
    /// <param name="ticker">證券代號。</param>
    /// <param name="from">起始日（含）。</param>
    /// <param name="to">結束日（含）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
