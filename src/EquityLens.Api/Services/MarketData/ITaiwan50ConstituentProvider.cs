namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// 0050 成分股提供者介面，取得元大台灣50 ETF 成分股清單。
/// </summary>
public interface ITaiwan50ConstituentProvider
{
    /// <summary>
    /// 取得 0050 所有成分股的股票代號與名稱。
    /// </summary>
    Task<IReadOnlyList<Taiwan50Constituent>> GetConstituentsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 0050 成分股資料。
/// </summary>
public sealed record Taiwan50Constituent(string StockCode, string Name);
