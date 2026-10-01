using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

/// <summary>只重播已保存的原始 HTTP 回應；沒有證據時回傳來源不可用。</summary>
internal sealed class FrozenPriceSourceHandler(string directory) : HttpMessageHandler
{
    private JsonDocument? _frame;
    private string? _hash;
    public void Select(string ticker)
    {
        _frame?.Dispose();
        var path = Path.Combine(directory, ticker + ".json");
        _frame = File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        _hash = _frame is null ? null : EquityLens.Api.Services.MarketPrices.TaiwanPriceImportCoordinator.Hash(_frame.RootElement.GetRawText());
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_frame is null) return Task.FromResult(Unavailable());
        var frame = _frame.RootElement;
        var uri = request.RequestUri!;
        var query = QueryHelpers.ParseQuery(uri.Query);
        string? body = null;
        if (uri.Host.Contains("yahoo")) body = Read(frame, "yahoo");
        else if (uri.Host.Contains("finmind") && query.TryGetValue("dataset", out var dataset) && dataset == "TaiwanStockPrice")
        {
            if (!query.TryGetValue("data_id", out var ticker) || ticker != frame.GetProperty("ticker").GetString())
                return Task.FromResult(Unavailable());
            if (query["start_date"] != "2019-11-01" || query["end_date"] != "2026-09-30")
                return Task.FromResult(Unavailable());
            body = Read(frame, "raw");
        }
        else
        {
            var evidenceJson = Read(frame, "official");
            if (evidenceJson is not null)
            {
                using var evidence = JsonDocument.Parse(evidenceJson);
                foreach (var e in evidence.RootElement.EnumerateArray())
                {
                    var source = e.GetProperty("source").GetString();
                    if (!e.TryGetProperty("snapshot", out var snapshot)) continue;
                    if (uri.Host.Contains("finmind") && source == "FinMind:" + query["dataset"].ToString() &&
                        query["start_date"] == e.GetProperty("from").GetString() && query["end_date"] == e.GetProperty("to").GetString())
                        body = snapshot.GetString();
                    else if (!uri.Host.Contains("finmind") && source is "TWSE" or "TPEX" &&
                        !uri.AbsolutePath.Contains("STOCK_DAY") && !uri.AbsolutePath.Contains("tradingStock"))
                        body = snapshot.GetString();
                    else if (!uri.Host.Contains("finmind") && source is "TWSE:daily" or "TPEX:daily" &&
                        e.TryGetProperty("month", out var month) && query.TryGetValue("date", out var requestedMonth) && requestedMonth.ToString().Replace("/", "") == month.GetString()!.Replace("-", ""))
                        body = snapshot.GetString();
                    if (body is not null) break;
                }
            }
        }
        if (body is null) return Task.FromResult(Unavailable());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("X-EquityLens-Replay-Origin", JsonSerializer.Serialize(new
            { replay = true, frameSha256 = _hash, capturedAtUtc = Read(frame, "captured_at_utc") }));
        return Task.FromResult(response);
    }
    private static string? Read(JsonElement e, string name) =>
        e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
    private static HttpResponseMessage Unavailable() => new(HttpStatusCode.ServiceUnavailable)
    { Content = new StringContent("""{"error":"No complete frozen source evidence for this request."}""") };
}
