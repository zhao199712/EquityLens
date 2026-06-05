using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.MarketData;

public sealed class AlphaVantageMarketDataProvider : IMarketDataProvider
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "NASDAQ",
        "NYSE",
        "AMEX",
        "US"
    };

    private readonly HttpClient _httpClient;
    private readonly AlphaVantageOptions _options;

    public AlphaVantageMarketDataProvider(HttpClient httpClient, IOptions<AlphaVantageOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string SourceName => "AlphaVantage";

    public bool Supports(string exchange) => SupportedExchanges.Contains(exchange);

    public async Task<IReadOnlyList<ExternalSecuritySearchResult>> SearchSecuritiesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Alpha Vantage API key is not configured.");
        }

        var url = QueryHelpers.AddQueryString("/query", new Dictionary<string, string?>
        {
            ["function"] = "SYMBOL_SEARCH",
            ["keywords"] = query,
            ["apikey"] = _options.ApiKey
        });

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("bestMatches", out var matches) || matches.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ExternalSecuritySearchResult>();
        foreach (var match in matches.EnumerateArray())
        {
            var ticker = ReadString(match, "1. symbol")?.Trim().ToUpperInvariant();
            var name = ReadString(match, "2. name")?.Trim();
            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new ExternalSecuritySearchResult(
                ticker,
                NormalizeExchange(ReadString(match, "4. region")),
                name,
                ReadString(match, "3. type"),
                string.IsNullOrWhiteSpace(ReadString(match, "8. currency")) ? "USD" : ReadString(match, "8. currency")!.Trim().ToUpperInvariant(),
                null,
                null,
                null,
                SourceName));
        }

        return results;
    }

    public async Task<IReadOnlyList<ImportedMarketPrice>> GetDailyPricesAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Alpha Vantage API key is not configured.");
        }

        var url = QueryHelpers.AddQueryString("/query", new Dictionary<string, string?>
        {
            ["function"] = "TIME_SERIES_DAILY",
            ["symbol"] = security.Ticker,
            ["outputsize"] = "full",
            ["apikey"] = _options.ApiKey
        });

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("Time Series (Daily)", out var series))
        {
            return [];
        }

        var prices = new List<ImportedMarketPrice>();
        foreach (var day in series.EnumerateObject())
        {
            if (!DateOnly.TryParse(day.Name, CultureInfo.InvariantCulture, out var date) || date < from || date > to)
            {
                continue;
            }

            var values = day.Value;
            prices.Add(new ImportedMarketPrice(
                date,
                ReadDecimal(values, "1. open"),
                ReadDecimal(values, "2. high"),
                ReadDecimal(values, "3. low"),
                ReadDecimal(values, "4. close"),
                null,
                ReadLong(values, "5. volume")));
        }

        return prices.OrderBy(x => x.Date).ToList();
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        return decimal.Parse(element.GetProperty(propertyName).GetString()!, CultureInfo.InvariantCulture);
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static string NormalizeExchange(string? region)
    {
        return string.Equals(region, "United States", StringComparison.OrdinalIgnoreCase)
            ? "US"
            : string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant().Replace(" ", "_");
    }
}
