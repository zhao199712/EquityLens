using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// FinMind 市場資料提供者，提供台股（TWSE/TPEX）證券搜尋、解析與每日價格查詢。
/// </summary>
public sealed class FinMindMarketDataProvider : IMarketDataProvider, ICorporateActionProvider
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "TWSE",
        "TPEX"
    };

    private readonly HttpClient _httpClient;
    private readonly FinMindOptions _options;

    /// <summary>
    /// 初始化 FinMind 市場資料提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客戶端。</param>
    /// <param name="options">FinMind 設定選項。</param>
    public FinMindMarketDataProvider(HttpClient httpClient, IOptions<FinMindOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "FinMind";

    /// <inheritdoc />
    public bool Supports(string exchange) => SupportedExchanges.Contains(exchange);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var queryParameters = new Dictionary<string, string?>
        {
            ["dataset"] = "TaiwanStockInfo"
        };

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            queryParameters["token"] = _options.Token;
        }

        var url = QueryHelpers.AddQueryString("/api/v4/data", queryParameters);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var normalizedQuery = query.Trim().ToUpperInvariant();
        var results = new List<ExternalSecuritySearchResult>();
        foreach (var item in data.EnumerateArray())
        {
            var ticker = ReadString(item, "stock_id")?.Trim().ToUpperInvariant();
            var name = ReadString(item, "stock_name")?.Trim();
            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!ticker.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var exchange = NormalizeExchange(ReadString(item, "type"));
            if (exchange is null)
            {
                continue;
            }

            results.Add(new ExternalSecuritySearchResult(
                ticker,
                exchange,
                name,
                "Equity",
                "TWD",
                null,
                ReadString(item, "industry_category"),
                null,
                SourceName));
        }

        return results.Take(20).ToList();
    }

    /// <inheritdoc />
    public async Task<ExternalSecuritySearchResult?> ResolveSecurityAsync(
        string ticker,
        string exchange,
        CancellationToken cancellationToken)
    {
        var results = await SearchSecuritiesAsync(ticker, cancellationToken);
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var normalizedExchange = exchange.Trim().ToUpperInvariant();

        return results.FirstOrDefault(x =>
            x.Ticker.Equals(normalizedTicker, StringComparison.OrdinalIgnoreCase) &&
            x.Exchange.Equals(normalizedExchange, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var queryParameters = new Dictionary<string, string?>
        {
            ["dataset"] = "TaiwanStockPrice",
            ["data_id"] = security.Ticker,
            ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            queryParameters["token"] = _options.Token;
        }

        var query = QueryHelpers.AddQueryString("/api/v4/data", queryParameters);

        using var response = await _httpClient.GetAsync(query, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var prices = new List<ImportedMarketPrice>();
        foreach (var item in data.EnumerateArray())
        {
            if (!DateOnly.TryParse(item.GetProperty("date").GetString(), CultureInfo.InvariantCulture, out var date) || date < from || date > to)
            {
                continue;
            }

            prices.Add(new ImportedMarketPrice(
                date,
                ReadDecimal(item, "open"),
                ReadDecimal(item, "max"),
                ReadDecimal(item, "min"),
                ReadDecimal(item, "close"),
                null,
                ReadLong(item, "Trading_Volume")));
        }

        return prices.OrderBy(x => x.Date).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var actions = new List<CorporateAction>();
        // 除權息：after_price 為除權息後理論價；少數「權」類事件的 reference_price 等於除權前價格，不能使用。
        foreach (var item in await GetDatasetAsync("TaiwanStockDividendResult", ticker, from, to, cancellationToken))
            actions.Add(new(ReadDate(item), "dividend:" + ReadString(item, "stock_or_cache_dividend"),
                ReadDecimal(item, "after_price"), ReadDecimal(item, "before_price")));
        foreach (var item in await GetDatasetAsync("TaiwanStockSplitPrice", ticker, from, to, cancellationToken))
            actions.Add(new(ReadDate(item), "split:" + ReadString(item, "type"),
                ReadDecimal(item, "after_price"), ReadDecimal(item, "before_price")));
        foreach (var item in await GetDatasetAsync("TaiwanStockCapitalReductionReferencePrice", ticker, from, to, cancellationToken))
            actions.Add(new(ReadDate(item), "capital_reduction",
                ReadDecimal(item, "PostReductionReferencePrice"), ReadDecimal(item, "ClosingPriceonTheLastTradingDay")));
        return actions.OrderBy(x => x.Date).ToList();
    }

    private async Task<IReadOnlyList<JsonElement>> GetDatasetAsync(
        string dataset, string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var queryParameters = new Dictionary<string, string?>
        {
            ["dataset"] = dataset,
            ["data_id"] = ticker,
            ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(_options.Token)) queryParameters["token"] = _options.Token;
        using var response = await _httpClient.GetAsync(QueryHelpers.AddQueryString("/api/v4/data", queryParameters), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        // 非 200 狀態或缺少 data 陣列代表回應不可信（例如額度用盡），不能當作「沒有公司行動」。
        if (document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
            throw new InvalidOperationException($"FinMind {dataset} returned status {status.GetInt32()}.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"FinMind {dataset} response has no data array.");
        return data.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static DateOnly ReadDate(JsonElement element) =>
        DateOnly.Parse(element.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.String
            ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
            : value.GetDecimal();
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.String
            ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture)
            : value.GetInt64();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static string? NormalizeExchange(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "TWSE";
        }

        if (type.Contains("otc", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("tpex", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("上櫃", StringComparison.OrdinalIgnoreCase))
        {
            return "TPEX";
        }

        return type.Contains("twse", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("上市", StringComparison.OrdinalIgnoreCase)
            ? "TWSE"
            : null;
    }
}
