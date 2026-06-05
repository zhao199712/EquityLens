using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.MarketData;

public sealed class FinMindMarketDataProvider : IMarketDataProvider
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "TWSE",
        "TPEX"
    };

    private readonly HttpClient _httpClient;
    private readonly FinMindOptions _options;

    public FinMindMarketDataProvider(HttpClient httpClient, IOptions<FinMindOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string SourceName => "FinMind";

    public bool Supports(string exchange) => SupportedExchanges.Contains(exchange);

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
