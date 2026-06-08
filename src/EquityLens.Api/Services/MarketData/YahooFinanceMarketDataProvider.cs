using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// Yahoo Finance 市場資料提供者，提供美股每日價格查詢。
/// 透過 Yahoo Finance Chart API 直接取得歷史日線資料，並支援重試與退避機制。
/// </summary>
public sealed class YahooFinanceMarketDataProvider : IMarketDataProvider
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "NASDAQ",
        "NYSE",
        "AMEX",
        "US"
    };

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
    public Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        // Yahoo Finance 目前不實作搜尋功能
        return Task.FromResult<IReadOnlyList<ExternalSecuritySearchResult>>([]);
    }

    /// <inheritdoc />
    public Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(
        string ticker,
        string exchange,
        CancellationToken cancellationToken)
    {
        // Yahoo Finance 目前不實作 metadata 解析
        return Task.FromResult<ExternalSecuritySearchResult?>(null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var yahooSymbol = ToYahooSymbol(security.Ticker, security.Exchange);
        var fromUnix = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var toUnix = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();

        var url = $"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?period1={fromUnix}&period2={toUnix}&interval=1d&events=history&includeAdjustedClose=true";

        var lastError = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                var delayMs = attempt * 1000;
                await Task.Delay(delayMs, cancellationToken);
            }

            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if ((int)response.StatusCode == 429)
                {
                    lastError = $"429 Too Many Requests (attempt {attempt + 1}/3)";
                    continue;
                }

                if ((int)response.StatusCode >= 500)
                {
                    lastError = $"{(int)response.StatusCode} Server Error (attempt {attempt + 1}/3)";
                    continue;
                }

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty("chart", out var chart))
                {
                    lastError = "Missing 'chart' in response";
                    continue;
                }

                if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                {
                    lastError = $"Yahoo Finance error: {error.GetRawText()}";
                    continue;
                }

                if (!chart.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array || resultArray.GetArrayLength() == 0)
                {
                    lastError = "Empty chart result";
                    continue;
                }

                var result = resultArray[0];
                if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
                {
                    lastError = "Missing timestamps in chart result";
                    continue;
                }

                var indicators = result.GetProperty("indicators");
                var quotes = indicators.GetProperty("quote");
                if (quotes.GetArrayLength() == 0)
                {
                    lastError = "Empty quote indicators";
                    continue;
                }

                var quote = quotes[0];
                var adjcloseList = indicators.TryGetProperty("adjclose", out var adjcloseArray) && adjcloseArray.GetArrayLength() > 0
                    ? adjcloseArray[0].GetProperty("adjclose")
                    : default;

                var prices = new List<ImportedMarketPrice>();
                var length = timestamps.GetArrayLength();

                for (int i = 0; i < length; i++)
                {
                    var timestamp = timestamps[i].GetInt64();
                    var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime.Date);

                    if (date < from || date > to)
                    {
                        continue;
                    }

                    var open = ReadDecimal(quote, "open", i);
                    var high = ReadDecimal(quote, "high", i);
                    var low = ReadDecimal(quote, "low", i);
                    var close = ReadDecimal(quote, "close", i);
                    var volume = ReadLong(quote, "volume", i);
                    var adjclose = adjcloseList.ValueKind != JsonValueKind.Undefined ? ReadDecimal(adjcloseList, i) : (decimal?)null;

                    if (!open.HasValue || !high.HasValue || !low.HasValue || !close.HasValue)
                    {
                        continue;
                    }

                    prices.Add(new ImportedMarketPrice(
                        date,
                        open.Value,
                        high.Value,
                        low.Value,
                        close.Value,
                        adjclose,
                        volume));
                }

                return prices.OrderBy(x => x.Date).ToList();
            }
            catch (HttpRequestException ex)
            {
                lastError = $"HTTP error: {ex.Message}";
            }
            catch (JsonException ex)
            {
                lastError = $"JSON parse error: {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                lastError = $"Yahoo error: {ex.Message}";
            }
        }

        throw new InvalidOperationException($"Yahoo Finance: {lastError}");
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
            "NASDAQ" or "NYSE" or "AMEX" or "US" => normalized,
            _ => normalized
        };
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
