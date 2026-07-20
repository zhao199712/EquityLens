using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// 從元大投信公開 0050 基金持股頁取得「基金權重－股票」成分。
/// 不接受任何替代股票池，資料格式或來源不可用時必須明確失敗。
/// </summary>
public sealed class YuantaTw0050ConstituentProvider : IYuantaTw0050ConstituentProvider
{
    private const string HoldingsUrl = "https://www.yuantaetfs.com/product/detail/0050/ratio";
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ConstituentRegex = new(
        @"(?<code>\d{4})\s+(?<name>[\p{IsCJKUnifiedIdeographs}A-Za-z0-9&\-（）()．.]+)",
        RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(
        @"(?:資料日期|更新日期|日期)\D{0,20}(?<year>20\d{2})[/-](?<month>\d{1,2})[/-](?<day>\d{1,2})",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public YuantaTw0050ConstituentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<YuantaTw0050ConstituentSnapshot> GetStockConstituentsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(HoldingsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var text = WhitespaceRegex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(html, " ")), " ").Trim();
        const string stockSectionMarker = "基金權重-股票";
        var stockSectionStart = text.IndexOf(stockSectionMarker, StringComparison.Ordinal);
        if (stockSectionStart < 0)
        {
            throw new InvalidOperationException("元大0050官方成分來源不可用或格式異常：找不到基金權重－股票區塊。");
        }

        var stockSectionEnd = text.IndexOf("基金權重-期貨", stockSectionStart, StringComparison.Ordinal);
        if (stockSectionEnd < 0)
        {
            stockSectionEnd = Math.Min(text.Length, stockSectionStart + 40000);
        }

        var stockSection = text[stockSectionStart..stockSectionEnd];
        var constituents = ConstituentRegex.Matches(stockSection)
            .Select(match => new Taiwan50Constituent(
                match.Groups["code"].Value,
                match.Groups["name"].Value.Trim()))
            .Where(x => x.StockCode is not "0050")
            .DistinctBy(x => x.StockCode)
            .ToList();

        // 0050 現行指數應為 50 檔；範圍檢查避免頁面錯誤時寫入錯誤股票池。
        if (constituents.Count is < 45 or > 55)
        {
            throw new InvalidOperationException(
                $"元大0050官方成分來源格式異常：解析到 {constituents.Count} 檔股票，預期約 50 檔。");
        }

        DateOnly? sourceAsOfDate = null;
        var dateMatch = DateRegex.Match(stockSection);
        if (dateMatch.Success && DateOnly.TryParseExact(
                $"{dateMatch.Groups["year"].Value}-{dateMatch.Groups["month"].Value}-{dateMatch.Groups["day"].Value}",
                "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            sourceAsOfDate = parsedDate;
        }

        return new YuantaTw0050ConstituentSnapshot(sourceAsOfDate, constituents);
    }
}
