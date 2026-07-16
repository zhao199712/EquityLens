using System.Text.Json;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.PortfolioValuations;
public sealed class FinMindPortfolioBenchmarkService(HttpClient http, IOptions<FinMindOptions> options, EquityLensDbContext db) : IPortfolioBenchmarkService
{
    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetTotalReturnIndexAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var lookupFrom = from.AddDays(-7);
        var cached = await db.TaiwanTotalReturnIndices.Where(x => x.TradingDate >= lookupFrom && x.TradingDate <= to).ToDictionaryAsync(x => x.TradingDate, x => x.Value, cancellationToken);
        var query = new Dictionary<string, string?> { ["dataset"] = "TaiwanStockTotalReturnIndex", ["data_id"] = "TAIEX", ["start_date"] = lookupFrom.ToString("yyyy-MM-dd"), ["end_date"] = to.ToString("yyyy-MM-dd") };
        if (!string.IsNullOrWhiteSpace(options.Value.Token)) query["token"] = options.Value.Token;
        using var response = await http.GetAsync(QueryHelpers.AddQueryString("/api/v4/data", query), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new Dictionary<DateOnly, decimal>();
        var values = cached;
        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("date", out var dateElement) || !DateOnly.TryParse(dateElement.GetString(), out var date)) continue;
            var valueElement = row.TryGetProperty("price", out var price) ? price : row.TryGetProperty("close", out var close) ? close : default;
            if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDecimal(out var value))
            {
                values[date] = value;
                var entity = await db.TaiwanTotalReturnIndices.SingleOrDefaultAsync(x => x.TradingDate == date, cancellationToken);
                if (entity is null) db.TaiwanTotalReturnIndices.Add(new TaiwanTotalReturnIndex { TradingDate = date, Value = value, UpdatedAtUtc = DateTime.UtcNow });
                else { entity.Value = value; entity.UpdatedAtUtc = DateTime.UtcNow; }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return values;
    }
}
