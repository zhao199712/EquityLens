using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// 市場資料提供者介面，提供證券搜尋、精準解析與每日價格查詢功能。
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// 提供者名稱，例如 "AlphaVantage" 或 "FinMind"。
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// 判斷是否支援指定交易所。
    /// </summary>
    /// <param name="exchange">交易所代碼。</param>
    /// <returns>若支援則返回 true，否則返回 false。</returns>
    bool Supports(string exchange);

    /// <summary>
    /// 依據關鍵字模糊搜尋證券。
    /// </summary>
    /// <param name="query">搜尋關鍵字。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>搜尋結果列表。</returns>
    Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依據股票代號與交易所精準解析單一證券。
    /// </summary>
    /// <param name="ticker">股票代號。</param>
    /// <param name="exchange">交易所代碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>若找到則返回外部證券搜尋結果，否則返回 null。</returns>
    Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(
        string ticker,
        string exchange,
        CancellationToken cancellationToken);

    /// <summary>
    /// 取得指定證券在日期區間內的每日市場價格。
    /// </summary>
    /// <param name="security">證券實體。</param>
    /// <param name="from">起始日期。</param>
    /// <param name="to">結束日期。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>每日市場價格列表。</returns>
    Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
