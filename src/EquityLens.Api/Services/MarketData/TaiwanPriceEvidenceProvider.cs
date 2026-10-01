using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.MarketData;

/// <summary>交易日、異常價格查證與官方衍生企業事件。任何不明確的證據均拒絕提交。</summary>
public sealed class TaiwanPriceEvidenceProvider(HttpClient http, IOptions<FinMindOptions> options) : ITaiwanPriceEvidenceProvider
{
    public string? LastEvidenceJson { get; private set; }

    public async Task<TaiwanPriceEvidence> GetEvidenceAsync(Security security, DateOnly from, DateOnly to,
        IReadOnlyList<ImportedMarketPrice> prices, bool requireActions, CancellationToken ct)
    {
        LastEvidenceJson = null;
        var snapshots = new List<object>();
        // 延伸查詢以證明區間尾端已被來源覆蓋，而非把過期曆表誤當休市。
        var calendar = await FinMindAsync("TaiwanStockTradingDate", null, from, to.AddDays(14), snapshots, ct);
        var calendarDates = calendar.EnumerateArray().Select(x => DateOnly.Parse(x.GetProperty("date").GetString()!, CultureInfo.InvariantCulture)).ToList();
        var dates = calendarDates.ToHashSet();
        if (dates.Count != calendarDates.Count) throw new PriceIntegrityException("Duplicate trading-calendar dates.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Taipei"));
        var completeDate = TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.UtcNow);
        var coverageEnd = to < completeDate ? to : completeDate;
        // 曆表必須涵蓋截止日（休市時延伸至後一交易日）；不能接受落後數日的曆表。
        if (dates.Count == 0 || dates.Max() < to)
            throw new PriceIntegrityException("Trading-calendar coverage is stale or empty.");
        dates.RemoveWhere(x => x > coverageEnd);
        var noTrades = new HashSet<DateOnly>();
        var corrections = new Dictionary<DateOnly, ImportedMarketPrice>();
        var map = prices.ToDictionary(x => x.Date);
        // 從要求起日查證，不能把來源第一筆之前的缺漏當成尚未上市或無成交。
        var anomalies = dates.Where(x => x >= from && x <= to &&
            (!map.TryGetValue(x, out var p) || p.Open <= 0 || p.High <= 0 || p.Low <= 0 || p.Close <= 0)).ToList();
        foreach (var month in anomalies.GroupBy(x => new DateOnly(x.Year, x.Month, 1)))
        {
            var official = await FetchMonthlyPricesAsync(security, month.Key, snapshots, ct);
            foreach (var date in month)
            {
                if (!official.TryGetValue(date, out var p))
                    throw new PriceIntegrityException($"Official source cannot verify missing/zero price on {date}.");
                if (p is null) noTrades.Add(date);
                else corrections.Add(date, p);
            }
        }

        var actions = new List<CorporatePriceAction>();
        if (requireActions)
        {
            foreach (var dataset in new[] { "TaiwanStockDividendResult", "TaiwanStockCapitalReductionReferencePrice", "TaiwanStockSplitPrice" })
            {
                var data = await FinMindAsync(dataset, security.Ticker, from, to, snapshots, ct);
                foreach (var row in data.EnumerateArray())
                {
                    if (row.GetProperty("stock_id").GetString() != security.Ticker)
                        throw new PriceIntegrityException("Corporate action ticker mismatch.");
                    var date = DateOnly.Parse(row.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);
                    if (date < from || date > to) throw new PriceIntegrityException("Out-of-range corporate action.");
                    var before = Number(row, "before_price");
                    var after = Number(row, row.TryGetProperty("after_price", out _) ? "after_price" : "reference_price");
                    if (before <= 0 || after <= 0) throw new PriceIntegrityException($"Invalid official factor on {date}.");
                    actions.Add(new(date, dataset, 0, after / before));
                }
            }
            // 僅限實際可查的官方區間；歷史部分保留 FinMind 官方衍生證據。
            var officialFrom = from;
            var fiveYears = today.AddYears(-5);
            if (security.Exchange == "TWSE" && officialFrom < fiveYears) officialFrom = fiveYears;
            if (officialFrom <= to)
            {
                var direct = await TryOfficialActionsAsync(security, officialFrom, to, snapshots, ct);
                foreach (var action in direct ?? [])
                {
                    var sameDate = actions.Where(x => x.Date == action.Date).ToList();
                    if (sameDate.Count != 1)
                        throw new PriceIntegrityException($"Official/derived event inventory conflict on {action.Date}.");
                    if (Math.Abs(sameDate[0].OfficialFactor!.Value - action.OfficialFactor!.Value) / action.OfficialFactor.Value > 0.001m)
                        throw new PriceIntegrityException($"Official/derived factor conflict on {action.Date}.");
                    actions.Remove(sameDate[0]);
                    actions.Add(action);
                }
                // 直接資料有成功的完整區間時，不能讓衍生除權息事件在官方區間內無對應。
                if (direct is not null && actions.Any(x => x.Kind == "TaiwanStockDividendResult" &&
                    x.Date >= officialFrom && !direct.Any(d => d.Date == x.Date)))
                    throw new PriceIntegrityException("Derived dividend has no corresponding official event.");
            }
            if (actions.GroupBy(x => x.Date).Any(x => x.Count() != 1))
                throw new PriceIntegrityException("Multiple same-day official records require a verified combined factor.");
        }
        return new(dates, actions.OrderBy(x => x.Date).ToList(), noTrades, corrections,
            JsonSerializer.Serialize(snapshots), requireActions);
    }

    private async Task<JsonElement> FinMindAsync(string dataset, string? ticker, DateOnly from, DateOnly to,
        List<object> evidence, CancellationToken ct)
    {
        var url = QueryHelpers.AddQueryString(options.Value.BaseUrl.TrimEnd('/') + "/api/v4/data",
            new Dictionary<string, string?> { ["dataset"] = dataset, ["data_id"] = ticker,
                ["start_date"] = from.ToString("yyyy-MM-dd"), ["end_date"] = to.ToString("yyyy-MM-dd") });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(options.Value.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("status", out var status) || status.GetInt32() != 200 ||
            !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException($"FinMind {dataset} did not return a complete success envelope.");
        evidence.Add(new { source = "FinMind:" + dataset, from, to, fetchedAtUtc = DateTime.UtcNow, snapshot = json });
        LastEvidenceJson = JsonSerializer.Serialize(evidence);
        return data.Clone();
    }

    private async Task<IReadOnlyList<CorporatePriceAction>?> TryOfficialActionsAsync(Security security,
        DateOnly from, DateOnly to, List<object> evidence, CancellationToken ct)
    {
        var url = security.Exchange == "TWSE"
            ? $"https://www.twse.com.tw/exchangeReport/TWT49U?response=json&startDate={from:yyyyMMdd}&endDate={to:yyyyMMdd}"
            : $"https://www.tpex.org.tw/www/zh-tw/bulletin/exDailyQ?response=json&startDate={from:yyyy/MM/dd}&endDate={to:yyyy/MM/dd}";
        string json;
        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            evidence.Add(new { source = security.Exchange, from, to, unavailable = true, fallback = "FinMind official-derived events" });
            return null;
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("stat", out var status) && !string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new { source = security.Exchange, from, to, snapshot = json, unavailable = true });
            return null;
        }
        var result = new List<CorporatePriceAction>();
        var recognized = false;
        foreach (var table in Tables(root))
        {
            if (!table.TryGetProperty("fields", out var fields) || !table.TryGetProperty("data", out var data)) continue;
            var names = fields.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            var dateIndex = Find(names, "資料日期", "除權息日期", "日期");
            var tickerIndex = Find(names, "股票代號", "股票代碼", "代號", "證券代號");
            var beforeIndex = Find(names, "除權息前收盤價", "除權息前收盤價格");
            var afterIndex = Find(names, "除權息參考價", "除權息參考價格");
            if (dateIndex < 0 || tickerIndex < 0 || beforeIndex < 0 || afterIndex < 0) continue;
            recognized = true;
            foreach (var row in data.EnumerateArray())
            {
                if (Text(row[tickerIndex]) != security.Ticker) continue;
                var date = ParseDate(Text(row[dateIndex]));
                var before = ParseNumber(Text(row[beforeIndex]));
                var after = ParseNumber(Text(row[afterIndex]));
                if (before <= 0 || after <= 0 || date < from || date > to)
                    throw new PriceIntegrityException("Invalid direct official corporate action.");
                result.Add(new(date, security.Exchange + "_combined", 0, after / before));
            }
        }
        if (!recognized) throw new PriceIntegrityException("Official action schema changed; completeness cannot be verified.");
        evidence.Add(new { source = security.Exchange, from, to, fetchedAtUtc = DateTime.UtcNow, snapshot = json });
        LastEvidenceJson = JsonSerializer.Serialize(evidence);
        return result;
    }

    private async Task<Dictionary<DateOnly, ImportedMarketPrice?>> FetchMonthlyPricesAsync(Security security,
        DateOnly month, List<object> evidence, CancellationToken ct)
    {
        var url = security.Exchange == "TWSE"
            ? $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={month:yyyyMMdd}&stockNo={Uri.EscapeDataString(security.Ticker)}"
            : $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?response=json&date={month:yyyy/MM/dd}&code={Uri.EscapeDataString(security.Ticker)}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("stat", out var status) && !string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
            throw new PriceIntegrityException("Official monthly prices are unavailable.");
        evidence.Add(new { source = security.Exchange + ":daily", ticker = security.Ticker, month,
            fetchedAtUtc = DateTime.UtcNow, snapshot = json });
        LastEvidenceJson = JsonSerializer.Serialize(evidence);
        var result = new Dictionary<DateOnly, ImportedMarketPrice?>();
        foreach (var table in Tables(document.RootElement))
        {
            if (!table.TryGetProperty("fields", out var fields) || !table.TryGetProperty("data", out var data)) continue;
            var names = fields.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            var di = Find(names, "日期", "資料日期");
            var oi = Find(names, "開盤價"); var hi = Find(names, "最高價"); var li = Find(names, "最低價"); var ci = Find(names, "收盤價");
            var vi = Find(names, "成交股數", "成交仟股");
            if (di < 0 || oi < 0 || hi < 0 || li < 0 || ci < 0) continue;
            foreach (var row in data.EnumerateArray())
            {
                var date = ParseDate(Text(row[di]));
                if (date.Year != month.Year || date.Month != month.Month)
                    throw new PriceIntegrityException("Official monthly response date mismatch.");
                var closeText = Text(row[ci]);
                var volume = vi < 0 ? (long?)null : (long)(ParseNumber(Text(row[vi])) * (names[vi].Contains("仟") ? 1000 : 1));
                if (closeText is "--" or "---" or "-" or "" && volume == 0)
                    result.Add(date, null);
                else
                    result.Add(date, new(date, ParseNumber(Text(row[oi])), ParseNumber(Text(row[hi])),
                        ParseNumber(Text(row[li])), ParseNumber(closeText), null, volume, security.Exchange + "Official"));
            }
        }
        if (result.Count == 0) throw new PriceIntegrityException("Official price schema/data is unavailable; no gap was filled.");
        return result;
    }

    private static IEnumerable<JsonElement> Tables(JsonElement root)
    {
        yield return root;
        if (root.TryGetProperty("tables", out var tables))
            foreach (var table in tables.EnumerateArray()) yield return table;
    }
    private static int Find(List<string> names, params string[] aliases) =>
        names.FindIndex(x => aliases.Contains(x.Replace("<br>", "").Replace(" ", "")));
    private static string Text(JsonElement x) => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "").Trim() : x.ToString();
    private static decimal Number(JsonElement row, string name) => ParseNumber(Text(row.GetProperty(name)));
    private static decimal ParseNumber(string s) => decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
    private static DateOnly ParseDate(string s)
    {
        s = s.Replace("年", "/").Replace("月", "/").Replace("日", "");
        var parts = s.Split('/');
        if (parts.Length != 3) return DateOnly.Parse(s, CultureInfo.InvariantCulture);
        var year = int.Parse(parts[0], CultureInfo.InvariantCulture);
        return new DateOnly(year < 1911 ? year + 1911 : year, int.Parse(parts[1]), int.Parse(parts[2]));
    }
}
