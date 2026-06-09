using EquityLens.Api.Contracts.DemoData;

namespace EquityLens.Api.Services.DemoData;

/// <summary>
/// 演示資料服務介面，提供演示資料的狀態查詢、種子資料建立與清除功能。
/// </summary>
public interface IDemoDataService
{
    /// <summary>
    /// 取得當前演示資料的種子狀態，包含投資組合、持倉、證券與市場價格的數量統計。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>演示資料狀態回應。</returns>
    Task<DemoDataStatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 建立演示資料，包含預設投資組合、持倉、證券與市場價格。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>種子資料建立結果，包含各類資料的新增與更新數量。</returns>
    Task<SeedDemoDataResponse> SeedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 清除所有演示資料（刪除預設投資組合及其持倉）。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>清除結果，包含已刪除的投資組合與持倉數量。</returns>
    Task<ClearDemoDataResponse> ClearAsync(CancellationToken cancellationToken);
}
