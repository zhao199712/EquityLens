using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>
/// 市場價格服務介面，提供證券價格查詢與每日價格導入功能。
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
    /// 為指定證券導入每日市場價格資料。
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
    /// 同步指定證券的每日市場價格資料（目前行為與導入相同）。
    /// </summary>
    /// <param name="securityId">證券的唯一識別碼。</param>
    /// <param name="request">同步價格的請求資料，包含日期區間。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回同步結果；
    /// 可能的錯誤碼與 <see cref="ImportDailyPricesAsync"/> 相同。
    /// </returns>
    Task<Result<ImportMarketPricesResponse>> SyncDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依據股票代號與交易所導入每日市場價格，若證券不存在則自動建立。
    /// </summary>
    /// <param name="request">依代號導入價格的請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>
    /// 成功時返回導入結果（包含是否建立新證券）；
    /// 若缺少必要欄位則返回錯誤碼 <c>security.required_fields</c>；
    /// 若日期區間無效則返回錯誤碼 <c>market_price.invalid_range</c>；
    /// 若交易所不支援則返回錯誤碼 <c>market_price.unsupported_exchange</c>。
    /// </returns>
    Task<Result<ImportMarketPricesByTickerResponse>> ImportDailyPricesByTickerAsync(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken);
}
