using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.Securities;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>
/// 市場價格服務介面，提供證券價格查詢、每日價格導入與同步功能。
/// </summary>
public interface IMarketPriceService
{
    /// <summary>
    /// 依據證券識別碼與日期區間查詢市場價格。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="from">起始日期，可為 null。</param>
    /// <param name="to">結束日期，可為 null。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回價格列表；
    /// 若日期區間無效則返回錯誤碼 <c>market_price.invalid_range</c>；
    /// 若證券不存在則返回錯誤碼 <c>security.not_found</c>。
    /// </returns>
    Task<Result<IReadOnlyList<MarketPriceResponse>>> GetPricesAsync(
        Guid securityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    /// <summary>
    /// 為指定證券導入指定日期區間的每日市場價格。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="request">導入價格的請求資料，包含日期區間。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回導入結果（包含資料來源與筆數統計）；
    /// 若日期區間無效則返回錯誤碼 <c>market_price.invalid_range</c>；
    /// 若證券不存在則返回錯誤碼 <c>security.not_found</c>；
    /// 若交易所不支援則返回錯誤碼 <c>market_price.unsupported_exchange</c>；
    /// 若資料提供者發生錯誤則返回 <c>market_price.provider_error</c> 等錯誤碼。
    /// </returns>
    Task<Result<ImportMarketPricesResponse>> ImportDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 智慧同步指定證券的每日市場價格。
    /// 若今天已同步過且未強制刷新，則略過不打外部 API。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="days">拉取最近幾天的日線資料。</param>
    /// <param name="force">是否強制同步，忽略今日已同步的檢查。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回同步結果，包含是否實際觸發同步；
    /// 若證券不存在則返回錯誤碼 <c>security.not_found</c>；
    /// 若交易所不支援則返回錯誤碼 <c>market_price.unsupported_exchange</c>。
    /// </returns>
    Task<Result<SyncMarketPricesResponse>> SyncDailyPricesAsync(
        Guid securityId,
        int days,
        bool force,
        CancellationToken cancellationToken);

    /// <summary>
    /// 批次刷新資料庫中既有證券的價格資料。
    /// 若今日已同步且未強制刷新，則略過。
    /// </summary>
    /// <param name="days">拉取最近幾天的日線資料。</param>
    /// <param name="force">是否強制刷新，忽略今日已同步的檢查。</param>
    /// <param name="limit">最多處理的證券數量。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>批次刷新結果統計。</returns>
    Task<RefreshSecuritiesPricesResponse> RefreshAllPricesAsync(
        int days,
        bool force,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依據股票代號與交易所導入每日市場價格。
    /// 證券必須已存在於資料庫，不會自動建立新證券。
    /// </summary>
    /// <param name="request">依代號導入價格的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回導入結果；
    /// 若缺少必要欄位則返回錯誤碼 <c>security.required_fields</c>；
    /// 若日期區間無效則返回錯誤碼 <c>market_price.invalid_range</c>；
    /// 若交易所不支援則返回錯誤碼 <c>market_price.unsupported_exchange</c>。
    /// </returns>
    Task<Result<ImportMarketPricesByTickerResponse>> ImportDailyPricesByTickerAsync(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken);
}
