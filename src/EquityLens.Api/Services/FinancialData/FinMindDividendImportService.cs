using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.FinancialData;

public sealed class FinMindDividendImportService : IFinMindDividendImportService
{
    private readonly EquityLensDbContext _db;
    private readonly HttpClient _http;
    private readonly FinMindOptions _options;
    public FinMindDividendImportService(EquityLensDbContext db, HttpClient http, IOptions<FinMindOptions> options) { _db = db; _http = http; _options = options.Value; }

    public async Task<FinMindDividendImportResult> ImportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var securities = await _db.Securities.Where(x => x.IsActive && (x.Exchange == "TWSE" || x.Exchange == "TPEX")).ToListAsync(ct);
        var eventCount = 0; var flowCount = 0;
        foreach (var security in securities)
        {
            var query = new Dictionary<string, string?> { ["dataset"] = "TaiwanStockDividend", ["data_id"] = security.Ticker, ["start_date"] = from.ToString("yyyy-MM-dd"), ["end_date"] = to.ToString("yyyy-MM-dd"), ["token"] = _options.Token };
            using var response = await _http.GetAsync(QueryHelpers.AddQueryString("/api/v4/data", query), ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            foreach (var row in data.EnumerateArray())
            {
                var cash = GetDecimal(row, "CashEarningsDistribution") + GetDecimal(row, "CashStatutorySurplus");
                var exText = GetString(row, "CashExDividendTradingDate");
                if (cash <= 0 || !DateOnly.TryParse(exText, CultureInfo.InvariantCulture, out var exDate)) continue;
                var payText = GetString(row, "CashDividendPaymentDate");
                DateOnly? payment = DateOnly.TryParse(payText, CultureInfo.InvariantCulture, out var paid) ? paid : null;
                var key = $"{security.Ticker}:{exDate:yyyy-MM-dd}:{cash.ToString(CultureInfo.InvariantCulture)}";
                var evt = await _db.CashDividendEvents.SingleOrDefaultAsync(x => x.Source == "FinMind" && x.SourceKey == key, ct);
                if (evt is null) { evt = new CashDividendEvent { SecurityId = security.Id, ExDividendDate = exDate, PaymentDate = payment, CashAmountPerShare = cash, SourceKey = key }; _db.CashDividendEvents.Add(evt); eventCount++; }
                else { evt.PaymentDate = payment; evt.CashAmountPerShare = cash; evt.UpdatedAtUtc = DateTime.UtcNow; }
                await _db.SaveChangesAsync(ct);
                var portfolioIds = await _db.PortfolioTransactions.Where(t => t.SecurityId == security.Id && t.TransactionDate <= exDate).Select(t => t.PortfolioId).Distinct().ToListAsync(ct);
                foreach (var portfolioId in portfolioIds)
                {
                    var txs = await _db.PortfolioTransactions.Where(t => t.PortfolioId == portfolioId && t.SecurityId == security.Id && t.TransactionDate <= exDate).ToListAsync(ct);
                    var qty = txs.Sum(t => t.TransactionType == "BUY" ? t.Quantity : -t.Quantity);
                    if (qty <= 0 || await _db.PortfolioCashFlows.AnyAsync(f => f.PortfolioId == portfolioId && f.CashDividendEventId == evt.Id, ct)) continue;
                    var effectiveDate = payment ?? exDate;
                    var status = effectiveDate <= DateOnly.FromDateTime(DateTime.UtcNow) ? "Posted" : "Scheduled";
                    _db.PortfolioCashFlows.Add(new PortfolioCashFlow { PortfolioId = portfolioId, SecurityId = security.Id, CashDividendEventId = evt.Id, FlowType = "Dividend", Amount = qty * cash, EffectiveDate = effectiveDate, Status = status, Note = $"FinMind 現金股利 {security.Ticker}" }); flowCount++;
                }
                await _db.SaveChangesAsync(ct);
            }
        }
        return new FinMindDividendImportResult(eventCount, flowCount);
    }
    private static string? GetString(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static decimal GetDecimal(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetDecimal() : 0m;
}
