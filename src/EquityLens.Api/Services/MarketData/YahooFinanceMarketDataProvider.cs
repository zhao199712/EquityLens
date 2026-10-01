using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// Yahoo Finance 市場資料提供者，提供美股每日價格查詢。
/// 透過 Yahoo Finance Chart API 直接取得歷史日線資料，並支援重試與退避機制。
/// </summary>
public sealed class YahooFinanceMarketDataProvider : IMarketDataProvider, IAdjustedCloseProvider
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "NASDAQ",
        "NYSE",
        "AMEX",
        "US"
    };

    public string? LastAdjustmentEvidenceJson { get; private set; }
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 Yahoo Finance 市場資料提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客戶端。</param>
    public YahooFinanceMarketDataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string SourceName => "YahooFinance";

    /// <inheritdoc />
    public bool Supports(string exchange) => SupportedExchanges.Contains(exchange);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = QueryHelpers.AddQueryString("/v1/finance/search", new Dictionary<string, string?>
        {
            ["q"] = query.Trim(),
            ["quotesCount"] = "10",
            ["newsCount"] = "0"
        });

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ExternalSecuritySearchResult>();
        foreach (var quote in quotes.EnumerateArray())
        {
            var yahooSymbol = ReadString(quote, "symbol")?.Trim();
            if (string.IsNullOrWhiteSpace(yahooSymbol))
            {
                continue;
            }

            var exchange = NormalizeYahooExchange(
                ReadString(quote, "exchDisp") ?? ReadString(quote, "exchange"));
            if (exchange is null || !Supports(exchange))
            {
                continue;
            }

            var ticker = FromYahooSymbol(yahooSymbol);
            var name = ReadString(quote, "longname")
                ?? ReadString(quote, "shortname")
                ?? ReadString(quote, "displayName")
                ?? ticker;
            var quoteType = ReadString(quote, "quoteType") ?? "EQUITY";

            results.Add(new ExternalSecuritySearchResult(
                ticker,
                exchange,
                name.Trim(),
                quoteType.Trim(),
                "USD",
                null,
                null,
                null,
                SourceName));
        }

        return results
            .GroupBy(x => new { x.Ticker, x.Exchange })
            .Select(x => x.First())
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(
        string ticker,
        string exchange,
        CancellationToken cancellationToken)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var normalizedExchange = exchange.Trim().ToUpperInvariant();
        var yahooSymbol = ToYahooSymbol(normalizedTicker, normalizedExchange);

        var searchResults = await SearchSecuritiesAsync(yahooSymbol, cancellationToken);
        var match = searchResults.FirstOrDefault(x =>
            TickerMatches(x.Ticker, normalizedTicker) &&
            ExchangeMatches(x.Exchange, normalizedExchange));
        if (match is not null)
        {
            return match;
        }

        return await ResolveFromChartMetaAsync(yahooSymbol, normalizedTicker, normalizedExchange, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var json = await FetchChartAsync(security, from, to, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var result = ChartResult(document.RootElement);
        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        ValidateArrays(quote, timestamps.GetArrayLength());
        var adjusted = ParseAdjustmentSnapshot(json, security.Exchange, from, to).AdjustedCloses;
        var prices = new List<ImportedMarketPrice>();
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            var date = ChartDate(result, timestamps[i].GetInt64(), security.Exchange);
            if (date < from || date > to) continue;
            var o = ReadDecimal(quote, "open", i);
            var h = ReadDecimal(quote, "high", i);
            var l = ReadDecimal(quote, "low", i);
            var c = ReadDecimal(quote, "close", i);
            if (o is null || h is null || l is null || c is null) continue;
            prices.Add(new(date, o.Value, h.Value, l.Value, c.Value,
                adjusted.TryGetValue(date, out var a) ? a : null, ReadLong(quote, "volume", i)));
        }
        return prices.OrderBy(x => x.Date).ToList();
    }

    public async Task<AdjustmentSnapshot> GetAdjustmentSnapshotAsync(
        Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ParseAdjustmentSnapshot(await FetchChartAsync(security, from, to, cancellationToken), security.Exchange, from, to);

    private async Task<string> FetchChartAsync(Security security, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LastAdjustmentEvidenceJson = null;
        var symbol = ToYahooSymbol(security.Ticker, security.Exchange);
        // 前後各一日，避免交易所時區把邊界日排除；解析後再依當地日期篩選。
        var start = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1d&events=div%2Csplits&includeAdjustedClose=true";
        for (var attempt = 0; ; attempt++)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < 2)
            {
                await Task.Delay((attempt + 1) * 1000, cancellationToken);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            LastAdjustmentEvidenceJson = json;
            using var document = JsonDocument.Parse(json);
            var result = ChartResult(document.RootElement);
            if (result.TryGetProperty("meta", out var meta) && meta.TryGetProperty("symbol", out var returnedSymbol) &&
                !string.Equals(returnedSymbol.GetString(), symbol, StringComparison.OrdinalIgnoreCase))
                throw new EquityLens.Api.Services.MarketPrices.PriceIntegrityException("Yahoo price ticker mismatch.");
            return json;
        }
    }

    public static AdjustmentSnapshot ParseAdjustmentSnapshot(string json, string exchange, DateOnly from, DateOnly to)
    {
        using var document = JsonDocument.Parse(json);
        var result = ChartResult(document.RootElement);
        var timestamps = result.GetProperty("timestamp");
        if (timestamps.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Invalid Yahoo timestamps.");
        var length = timestamps.GetArrayLength();
        var indicators = result.GetProperty("indicators");
        if (indicators.TryGetProperty("quote", out var quotes) && quotes.GetArrayLength() > 0)
            ValidateArrays(quotes[0], length);
        var adjusted = indicators.TryGetProperty("adjclose", out var list) && list.GetArrayLength() > 0
            ? list[0].GetProperty("adjclose") : default;
        if (adjusted.ValueKind != JsonValueKind.Undefined &&
            (adjusted.ValueKind != JsonValueKind.Array || adjusted.GetArrayLength() != length))
            throw new InvalidOperationException("Yahoo adjusted-close/timestamp lengths differ.");
        var closes = new Dictionary<DateOnly, decimal>();
        var dates = new HashSet<DateOnly>();
        DateOnly? previous = null;
        for (var i = 0; i < length; i++)
        {
            var date = ChartDate(result, timestamps[i].GetInt64(), exchange);
            if (!dates.Add(date) || previous >= date) throw new InvalidOperationException("Yahoo dates duplicate or unordered.");
            previous = date;
            if (date < from || date > to) continue;
            var value = ReadDecimal(adjusted, i);
            if (value is <= 0) throw new InvalidOperationException($"Nonpositive Yahoo adjusted close on {date}.");
            if (value.HasValue) closes.Add(date, value.Value);
        }
        var actions = new List<CorporatePriceAction>();
        if (result.TryGetProperty("events", out var events))
        {
            foreach (var kind in new[] { "dividends", "splits" })
            {
                if (!events.TryGetProperty(kind, out var entries)) continue;
                foreach (var entry in entries.EnumerateObject())
                {
                    var item = entry.Value;
                    var date = ChartDate(result, item.GetProperty("date").GetInt64(), exchange);
                    if (date < from || date > to) continue;
                    var value = kind == "dividends" ? item.GetProperty("amount").GetDecimal()
                        : item.GetProperty("numerator").GetDecimal() / item.GetProperty("denominator").GetDecimal();
                    actions.Add(new(date, kind == "dividends" ? "cash" : "split", value));
                }
            }
        }
        return new(closes, actions, json, DateTime.UtcNow, true);
    }

    private static JsonElement ChartResult(JsonElement root)
    {
        var chart = root.GetProperty("chart");
        if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Yahoo chart reports an error.");
        var results = chart.GetProperty("result");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1)
            throw new InvalidOperationException("Yahoo chart must contain one result.");
        return results[0];
    }

    private static DateOnly ChartDate(JsonElement result, long timestamp, string exchange)
    {
        var instant = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var zone = exchange is "TWSE" or "TPEX" ? "Asia/Taipei" : null;
        if (result.TryGetProperty("meta", out var meta) && meta.TryGetProperty("exchangeTimezoneName", out var timezone))
            zone = timezone.GetString() ?? zone;
        if (zone is not null)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        return DateOnly.FromDateTime(instant.UtcDateTime);
    }

    private static void ValidateArrays(JsonElement quote, int length)
    {
        foreach (var name in new[] { "open", "high", "low", "close", "volume" })
            if (quote.TryGetProperty(name, out var values) &&
                (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != length))
                throw new InvalidOperationException($"Yahoo {name}/timestamp lengths differ.");
    }

    private static string ToYahooSymbol(string ticker, string exchange)
    {
        var normalized = ticker.Trim().ToUpperInvariant();

        // BRKB / BRK-B 映射
        if (normalized == "BRKB")
        {
            return "BRK-B";
        }

        return exchange.ToUpperInvariant() switch
        {
            "TWSE" => $"{normalized}.TW",
            "TPEX" => $"{normalized}.TWO",
            _ => normalized
        };
    }

    private async Task<ExternalSecuritySearchResult?> ResolveFromChartMetaAsync(
        string yahooSymbol,
        string normalizedTicker,
        string normalizedExchange,
        CancellationToken cancellationToken)
    {
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=1d&interval=1d";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("chart", out var chart) ||
            chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null ||
            !chart.TryGetProperty("result", out var resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            return null;
        }

        var result = resultArray[0];
        if (!result.TryGetProperty("meta", out var meta))
        {
            return null;
        }

        var symbol = ReadString(meta, "symbol") ?? yahooSymbol;
        var ticker = FromYahooSymbol(symbol);
        if (!TickerMatches(ticker, normalizedTicker))
        {
            return null;
        }

        var exchange = NormalizeYahooExchange(ReadString(meta, "exchangeName") ?? ReadString(meta, "fullExchangeName"));
        if (exchange is null || !ExchangeMatches(exchange, normalizedExchange))
        {
            return null;
        }

        var assetType = ReadString(meta, "instrumentType") ?? "EQUITY";
        var currency = ReadString(meta, "currency") ?? "USD";
        return new ExternalSecuritySearchResult(
            ticker,
            exchange,
            ticker,
            assetType,
            currency,
            null,
            null,
            null,
            SourceName);
    }

    private static string FromYahooSymbol(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return normalized == "BRK-B" ? "BRKB" : normalized;
    }

    private static bool TickerMatches(string candidate, string expected)
    {
        return string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ToYahooSymbol(candidate, "US"), ToYahooSymbol(expected, "US"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExchangeMatches(string candidate, string expected)
    {
        var normalizedCandidate = candidate.Trim().ToUpperInvariant();
        var normalizedExpected = expected.Trim().ToUpperInvariant();
        return normalizedCandidate == normalizedExpected ||
            normalizedExpected == "US" && normalizedCandidate is "NASDAQ" or "NYSE" or "AMEX";
    }

    private static string? NormalizeYahooExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return null;
        }

        return exchange.Trim().ToUpperInvariant() switch
        {
            "NASDAQ" or "NMS" or "NCM" or "NGM" => "NASDAQ",
            "NYSE" or "NYQ" => "NYSE",
            "AMEX" or "ASE" => "AMEX",
            "US" => "US",
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName, int index)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (index >= array.GetArrayLength())
        {
            return null;
        }

        var value = array[index];
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    }

    private static decimal? ReadDecimal(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength())
        {
            return null;
        }

        var value = element[index];
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    }

    private static long? ReadLong(JsonElement element, string propertyName, int index)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (index >= array.GetArrayLength())
        {
            return null;
        }

        var value = array[index];
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt64(out var parsed) ? parsed : null;
    }
}
