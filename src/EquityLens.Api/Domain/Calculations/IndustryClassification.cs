namespace EquityLens.Api.Domain.Calculations;

/// <summary>集中度使用已知大類；保留來源原值，不由股票代號推測細分類。</summary>
public static class IndustryClassification
{
    public const string Version = "broad-industry-v1";
    public static string? Raw(string? industry, string? sector = null) =>
        string.IsNullOrWhiteSpace(industry) ? string.IsNullOrWhiteSpace(sector) ? null : sector : industry;

    public static string Normalize(string? industry, string? sector = null)
    {
        var value = Raw(industry, sector)?.Trim();
        return value switch
        {
            null or "" or "未分類" => "未分類",
            "電子工業" or "電子業" or "半導體業" or "半導體" or "電腦及週邊設備業" or "光電業"
                or "通信網路業" or "電子零組件業" or "電子通路業" or "資訊服務業" or "其他電子業" => "電子工業",
            "金融" or "金融保險業" or "金融業" or "金融保險" => "金融保險業",
            _ => value // 未知分類原樣保留，避免猜測對應。
        };
    }
}
