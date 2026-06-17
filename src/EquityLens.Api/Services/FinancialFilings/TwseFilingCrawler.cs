using System.Text;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace EquityLens.Api.Services.FinancialFilings;

/// <summary>
/// TWSE 財報爬蟲實現，從證交所電子資料查詢作業下載財報 PDF。
/// </summary>
public sealed partial class TwseFilingCrawler : ITwseFilingCrawler
{
    private const string QueryBaseUrl = "https://doc.twse.com.tw/server-java/t57sb01";

    private static readonly Encoding Big5Encoding;

    static TwseFilingCrawler()
    {
        // 註冊 CodePages 編碼提供者以支援 Big5
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Big5Encoding = Encoding.GetEncoding("big5");
    }

    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 TWSE 財報爬蟲。
    /// </summary>
    /// <param name="httpClient">HTTP 客戶端。</param>
    public TwseFilingCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TwseFilingInfo>> GetAvailableFilingsAsync(
        string stockCode, int year, CancellationToken cancellationToken = default)
    {
        var url = $"{QueryBaseUrl}?step=1&colorchg=1&co_id={stockCode}&year={year}&seamon=&mtype=A&";
        var htmlBytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        var html = Big5Encoding.GetString(htmlBytes);

        return ParseFilingTable(html, stockCode, year);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPdfAsync(
        TwseFilingInfo filing, CancellationToken cancellationToken = default)
    {
        // Step 1: POST 表單取得 PDF 下載連結
        var formData = new Dictionary<string, string>
        {
            ["colorchg"] = "1",
            ["step"] = "9",
            ["kind"] = "A",
            ["co_id"] = filing.StockCode,
            ["filename"] = filing.FileName
        };

        using var formContent = new FormUrlEncodedContent(formData);
        using var postResponse = await _httpClient.PostAsync(QueryBaseUrl, formContent, cancellationToken);
        postResponse.EnsureSuccessStatusCode();

        var responseBytes = await postResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseHtml = Big5Encoding.GetString(responseBytes);

        // Step 2: 解析回應取得 PDF 連結
        var pdfLink = ExtractPdfLink(responseHtml);
        if (string.IsNullOrEmpty(pdfLink))
            throw new InvalidOperationException($"無法從回應中解析 PDF 連結。File: {filing.FileName}");

        var pdfUrl = $"https://doc.twse.com.tw{pdfLink}";

        // Step 3: 下載 PDF
        return await _httpClient.GetByteArrayAsync(pdfUrl, cancellationToken);
    }

    /// <summary>
    /// 解析 TWSE 查詢頁面的 HTML 表格，提取財報檔案資訊。
    /// </summary>
    private static IReadOnlyList<TwseFilingInfo> ParseFilingTable(
        string html, string stockCode, int year)
    {
        var results = new List<TwseFilingInfo>();

        // 逐列匹配：找到每列的季度、描述、檔案名、大小、日期
        var rowMatches = RowRegex().Matches(html);

        foreach (Match rowMatch in rowMatches)
        {
            var rowHtml = rowMatch.Value;

            // 跳過 header 列
            if (rowHtml.Contains("<th>")) continue;

            // 提取季度
            var quarterMatch = QuarterRegex().Match(rowHtml);
            if (!quarterMatch.Success) continue;
            var yearQuarter = quarterMatch.Groups[1].Value;
            var quarter = ExtractQuarter(yearQuarter);
            if (quarter != "Q4") continue;

            // 提取描述（IFRSs合併財報 / IFRSs英文版-合併財報 等）
            var descMatch = DescriptionRegex().Match(rowHtml);
            if (!descMatch.Success) continue;
            var description = descMatch.Groups[1].Value;
            if (!description.Contains("IFRSs") || !description.Contains("合併")) continue;
            if (description.Contains("英文版")) continue;

            // 提取 PDF 檔名
            var fileMatch = FileNameRegex().Match(rowHtml);
            if (!fileMatch.Success) continue;
            var fileName = fileMatch.Groups[1].Value;

            // 提取檔案大小
            var sizeMatch = FileSizeRegex().Match(rowHtml);
            var fileSizeStr = sizeMatch.Success ? sizeMatch.Groups[1].Value.Replace(",", "").Trim() : "0";
            long.TryParse(fileSizeStr, out var fileSizeBytes);

            // 提取上傳日期
            var dateMatch = UploadDateRegex().Match(rowHtml);
            var uploadDateStr = dateMatch.Success ? dateMatch.Groups[1].Value : "";
            DateTime.TryParse(uploadDateStr.Replace("/", "-"), out var uploadedAt);

            results.Add(new TwseFilingInfo(
                stockCode, year, quarter, fileName, description, fileSizeBytes, uploadedAt));
        }

        return results;
    }

    /// <summary>
    /// 從回應 HTML 中提取 PDF 下載連結。
    /// </summary>
    private static string ExtractPdfLink(string html)
    {
        var match = PdfLinkRegex().Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// 從「112 年 第一季」格式中提取季度。
    /// </summary>
    private static string ExtractQuarter(string yearQuarter)
    {
        if (yearQuarter.Contains("第一季")) return "Q1";
        if (yearQuarter.Contains("第二季")) return "Q2";
        if (yearQuarter.Contains("第三季")) return "Q3";
        if (yearQuarter.Contains("第四季")) return "Q4";
        return "Q1";
    }

    [GeneratedRegex("<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();

    [GeneratedRegex("href=['\"](/pdf/[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
    private static partial Regex PdfLinkRegex();

    [GeneratedRegex("<td[^>]*>(\\d{3}\\s*年\\s*第[一二三四]季)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex QuarterRegex();

    [GeneratedRegex("<td[^>]*>(IFRSs[^<]*)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex("readfile2\\([^,]+,[^,]+,\"([^\"]+)\"\\)", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex("<td[^>]*align='right'[^>]*>\\s*([\\d,]+)\\s*</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FileSizeRegex();

    [GeneratedRegex("<td[^>]*>\\s*(\\d{2,3}/\\d{2}/\\d{2}[^<]*)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UploadDateRegex();
}
