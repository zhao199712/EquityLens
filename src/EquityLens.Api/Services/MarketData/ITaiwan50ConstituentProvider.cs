namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// 富櫃50成分股提供者介面。這不是元大台灣50 ETF（0050）的成分來源。
/// </summary>
public interface ITaiwan50ConstituentProvider
{
    /// <summary>
    /// 取得富櫃50所有成分股的股票代號與名稱。
    /// </summary>
    Task<IReadOnlyList<Taiwan50Constituent>> GetConstituentsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 0050 成分股資料。
/// </summary>
public sealed record Taiwan50Constituent(string StockCode, string Name);

/// <summary>
/// 元大台灣50 ETF（0050）現行股票成分來源。僅供 0050 同步工作使用。
/// </summary>
public interface IYuantaTw0050ConstituentProvider
{
    Task<YuantaTw0050ConstituentSnapshot> GetStockConstituentsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record YuantaTw0050ConstituentSnapshot(
    DateOnly? SourceAsOfDate,
    IReadOnlyList<Taiwan50Constituent> Constituents);
