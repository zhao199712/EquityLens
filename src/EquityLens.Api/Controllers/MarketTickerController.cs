using System.Text.Json;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Services.MarketData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 行情跑馬燈控制器,提供加權指數、0050 與其前五大成分股的最新收盤價與漲跌幅。
/// </summary>
[Authorize]
[ApiController]
[Route("api/market/ticker")]
public sealed class MarketTickerController : ApiControllerBase
{
    private static readonly string[] ConstituentTickers = ["2330", "2317", "2454", "2308", "2382"];

    private readonly EquityLensDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinMindOptions _finMindOptions;

    /// <summary>
    /// 初始化行情跑馬燈控制器。
    /// </summary>
    public MarketTickerController(
        EquityLensDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IOptions<FinMindOptions> finMindOptions)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _finMindOptions = finMindOptions.Value;
    }

    /// <summary>
    /// 取得加權指數(TAIEX)、0050 ETF 與 0050 前五大成分股的最新行情快照。
    /// 指數取自 FinMind;個股與 ETF 取資料庫最近兩個交易日的收盤價。
    /// </summary>
    /// <param name="cancellationToken">取消權杖。</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketTickerEntry>>> GetTicker(CancellationToken cancellationToken)
    {
        var entries = new List<MarketTickerEntry>();

        var taiex = await TryGetTaiexAsync(cancellationToken);
        if (taiex is not null) entries.Add(taiex);

        var tickers = new[] { "0050" }.Concat(ConstituentTickers).ToList();
        var securities = await _dbContext.Securities
            .Where(x => tickers.Contains(x.Ticker) && x.Exchange == "TWSE")
            .ToListAsync(cancellationToken);

        foreach (var ticker in tickers)
        {
            var security = securities.FirstOrDefault(x => x.Ticker == ticker);
            if (security is null) continue;

            var lastTwo = await _dbContext.MarketPrices
                .Where(x => x.SecurityId == security.Id && x.Interval == "1d")
                .OrderByDescending(x => x.PriceTime)
                .Take(2)
                .Select(x => new { x.Close })
                .ToListAsync(cancellationToken);

            if (lastTwo.Count == 0 || lastTwo[0].Close <= 0) continue;
            decimal? changePct = lastTwo.Count == 2 && lastTwo[1].Close > 0
                ? Math.Round((lastTwo[0].Close / lastTwo[1].Close - 1m) * 100m, 2)
                : null;
            entries.Add(new MarketTickerEntry(security.Ticker, security.Name, lastTwo[0].Close, changePct));
        }

        return Ok(entries);
    }

    private async Task<MarketTickerEntry?> TryGetTaiexAsync(CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = new Dictionary<string, string?>
            {
                ["dataset"] = "TaiwanStockPrice",
                ["data_id"] = "TAIEX",
                ["start_date"] = today.AddDays(-10).ToString("yyyy-MM-dd"),
                ["end_date"] = today.ToString("yyyy-MM-dd"),
            };
            if (!string.IsNullOrWhiteSpace(_finMindOptions.Token)) query["token"] = _finMindOptions.Token;

            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(
                QueryHelpers.AddQueryString($"{_finMindOptions.BaseUrl.TrimEnd('/')}/api/v4/data", query),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;

            var points = new List<(DateOnly Date, decimal Close)>();
            foreach (var row in data.EnumerateArray())
            {
                if (!row.TryGetProperty("date", out var dateElement) || !DateOnly.TryParse(dateElement.GetString(), out var date)) continue;
                if (!row.TryGetProperty("close", out var closeElement) || closeElement.ValueKind != JsonValueKind.Number || !closeElement.TryGetDecimal(out var close)) continue;
                points.Add((date, close));
            }

            var ordered = points.OrderBy(x => x.Date).ToList();
            if (ordered.Count == 0 || ordered[^1].Close <= 0) return null;
            decimal? changePct = ordered.Count >= 2 && ordered[^2].Close > 0
                ? Math.Round((ordered[^1].Close / ordered[^2].Close - 1m) * 100m, 2)
                : null;
            return new MarketTickerEntry("TAIEX", "加權指數", ordered[^1].Close, changePct);
        }
        catch
        {
            return null;
        }
    }
}
