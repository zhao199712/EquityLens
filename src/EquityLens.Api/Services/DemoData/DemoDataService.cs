using System.Globalization;
using EquityLens.Api.Contracts.DemoData;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.DemoUser;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.DemoData;

/// <summary>
/// 演示資料服務實現，提供演示資料的狀態查詢、種子資料建立與清除功能。
/// </summary>
public sealed class DemoDataService : IDemoDataService
{
    private const string DemoPriceDataSource = "DemoCsv";
    private const string DemoPriceCsvPath = "data/demo-market-prices-2025-06-03-2026-06-03.csv";

    private static readonly DemoPortfolioDefinition[] DemoPortfolios =
    [
        new DemoPortfolioDefinition(
            "US Growth Portfolio",
            "Demo portfolio for US technology and ETF holdings.",
            "USD",
            [
                new DemoHoldingDefinition("AAPL", "NASDAQ", 10, 180, "USD"),
                new DemoHoldingDefinition("MSFT", "NASDAQ", 5, 390, "USD"),
                new DemoHoldingDefinition("NVDA", "NASDAQ", 8, 110, "USD"),
                new DemoHoldingDefinition("QQQ", "NASDAQ", 12, 460, "USD")
            ]),
        new DemoPortfolioDefinition(
            "Taiwan Core Portfolio",
            "Demo portfolio for Taiwan large-cap holdings.",
            "TWD",
            [
                new DemoHoldingDefinition("2330", "TWSE", 10, 900, "TWD"),
                new DemoHoldingDefinition("2454", "TWSE", 2, 1200, "TWD"),
                new DemoHoldingDefinition("0050", "TWSE", 20, 180, "TWD")
            ])
    ];

    private readonly EquityLensDbContext _dbContext;
    private readonly IDemoUserContext _demoUserContext;

    /// <summary>
    /// 初始化演示資料服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="demoUserContext">演示使用者內容。</param>
    public DemoDataService(EquityLensDbContext dbContext, IDemoUserContext demoUserContext)
    {
        _dbContext = dbContext;
        _demoUserContext = demoUserContext;
    }

    /// <inheritdoc />
    public async Task<DemoDataStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var demoPortfolioNames = DemoPortfolios.Select(x => x.Name).ToArray();
        var portfolioIds = await _dbContext.Portfolios
            .AsNoTracking()
            .Where(x => x.OwnerUserId == _demoUserContext.UserId && demoPortfolioNames.Contains(x.Name) && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var holdingCount = portfolioIds.Count == 0
            ? 0
            : await _dbContext.PortfolioHoldings.CountAsync(x => portfolioIds.Contains(x.PortfolioId), cancellationToken);

        var demoTickers = DemoPortfolios
            .SelectMany(x => x.Holdings)
            .Select(x => new { x.Ticker, x.Exchange })
            .Distinct()
            .ToList();
        var securityCount = 0;
        foreach (var demoTicker in demoTickers)
        {
            if (await _dbContext.Securities.AnyAsync(x => x.Ticker == demoTicker.Ticker && x.Exchange == demoTicker.Exchange && x.IsActive, cancellationToken))
            {
                securityCount++;
            }
        }

        var marketPriceCount = await _dbContext.MarketPrices.CountAsync(x => x.DataSource == DemoPriceDataSource, cancellationToken);

        return new DemoDataStatusResponse(
            portfolioIds.Count == DemoPortfolios.Length && holdingCount > 0,
            portfolioIds.Count,
            holdingCount,
            securityCount,
            marketPriceCount);
    }

    /// <inheritdoc />
    public async Task<SeedDemoDataResponse> SeedAsync(CancellationToken cancellationToken)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var csvRows = await ReadDemoPriceCsvAsync(cancellationToken);
        var securityResults = await EnsureSecuritiesAsync(csvRows, cancellationToken);
        var portfolioResults = await EnsurePortfoliosAndHoldingsAsync(securityResults.SecurityByTickerExchange, cancellationToken);
        var priceResults = await UpsertMarketPricesAsync(csvRows, securityResults.SecurityByTickerExchange, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SeedDemoDataResponse(
            portfolioResults.PortfoliosCreated,
            portfolioResults.HoldingsCreated,
            securityResults.SecuritiesCreated,
            priceResults.Inserted,
            priceResults.Updated);
    }

    /// <inheritdoc />
    public async Task<ClearDemoDataResponse> ClearAsync(CancellationToken cancellationToken)
    {
        var demoPortfolioNames = DemoPortfolios.Select(x => x.Name).ToArray();
        var portfolios = await _dbContext.Portfolios
            .Include(x => x.Holdings)
            .Where(x => x.OwnerUserId == _demoUserContext.UserId && demoPortfolioNames.Contains(x.Name))
            .ToListAsync(cancellationToken);

        var holdingsRemoved = portfolios.Sum(x => x.Holdings.Count);
        var portfoliosRemoved = portfolios.Count;

        _dbContext.Portfolios.RemoveRange(portfolios);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ClearDemoDataResponse(portfoliosRemoved, holdingsRemoved);
    }

    // 確保 CSV 中所有證券已存在於資料庫中，不存在則建立；返回證券對照表與建立數量
    private async Task<SecuritySeedResult> EnsureSecuritiesAsync(
        IReadOnlyList<DemoPriceCsvRow> csvRows,
        CancellationToken cancellationToken)
    {
        var securitiesCreated = 0;
        var securityByTickerExchange = new Dictionary<string, Security>(StringComparer.OrdinalIgnoreCase);

        foreach (var securityRow in csvRows.GroupBy(x => SecurityKey(x.Ticker, x.Exchange)).Select(x => x.First()))
        {
            var security = await _dbContext.Securities.SingleOrDefaultAsync(
                x => x.Ticker == securityRow.Ticker && x.Exchange == securityRow.Exchange,
                cancellationToken);

            if (security is null)
            {
                security = new Security
                {
                    Id = Guid.NewGuid(),
                    Ticker = securityRow.Ticker,
                    Exchange = securityRow.Exchange,
                    Name = securityRow.Name,
                    AssetType = InferAssetType(securityRow.Ticker, securityRow.Name),
                    Currency = securityRow.Exchange is "TWSE" or "TPEX" ? "TWD" : "USD",
                    IsActive = true
                };
                _dbContext.Securities.Add(security);
                securitiesCreated++;
            }
            else if (!security.IsActive)
            {
                security.IsActive = true;
            }

            securityByTickerExchange[SecurityKey(security.Ticker, security.Exchange)] = security;
        }

        return new SecuritySeedResult(securityByTickerExchange, securitiesCreated);
    }

    // 確保演示投資組合與持倉已存在，不存在則建立；返回建立數量統計
    private async Task<PortfolioSeedResult> EnsurePortfoliosAndHoldingsAsync(
        IReadOnlyDictionary<string, Security> securityByTickerExchange,
        CancellationToken cancellationToken)
    {
        var portfoliosCreated = 0;
        var holdingsCreated = 0;
        var now = DateTime.UtcNow;

        foreach (var definition in DemoPortfolios)
        {
            var portfolio = await _dbContext.Portfolios
                .Include(x => x.Holdings)
                .SingleOrDefaultAsync(x => x.OwnerUserId == _demoUserContext.UserId && x.Name == definition.Name, cancellationToken);

            if (portfolio is null)
            {
                portfolio = new Portfolio
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = _demoUserContext.UserId,
                    Name = definition.Name,
                    Description = definition.Description,
                    BaseCurrency = definition.BaseCurrency,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.Portfolios.Add(portfolio);
                portfoliosCreated++;
            }
            else
            {
                portfolio.Description = definition.Description;
                portfolio.BaseCurrency = definition.BaseCurrency;
                portfolio.IsActive = true;
                portfolio.UpdatedAtUtc = now;
            }

            foreach (var holdingDefinition in definition.Holdings)
            {
                if (!securityByTickerExchange.TryGetValue(SecurityKey(holdingDefinition.Ticker, holdingDefinition.Exchange), out var security))
                {
                    continue;
                }

                if (portfolio.Holdings.Any(x => x.SecurityId == security.Id))
                {
                    continue;
                }

                portfolio.Holdings.Add(new PortfolioHolding
                {
                    Id = Guid.NewGuid(),
                    PortfolioId = portfolio.Id,
                    SecurityId = security.Id,
                    Quantity = holdingDefinition.Quantity,
                    AverageCost = holdingDefinition.AverageCost,
                    CostCurrency = holdingDefinition.CostCurrency,
                    Note = "Demo holding",
                    UpdatedAtUtc = now
                });
                holdingsCreated++;
            }
        }

        return new PortfolioSeedResult(portfoliosCreated, holdingsCreated);
    }

    // 將 CSV 中的市場價格資料寫入資料庫，已存在則更新；返回新增與更新數量
    private async Task<MarketPriceSeedResult> UpsertMarketPricesAsync(
        IReadOnlyList<DemoPriceCsvRow> csvRows,
        IReadOnlyDictionary<string, Security> securityByTickerExchange,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;

        foreach (var rowsBySecurity in csvRows.GroupBy(x => SecurityKey(x.Ticker, x.Exchange)))
        {
            if (!securityByTickerExchange.TryGetValue(rowsBySecurity.Key, out var security))
            {
                continue;
            }

            var priceTimes = rowsBySecurity
                .Select(x => x.PriceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                .ToList();
            var existingPrices = await _dbContext.MarketPrices
                .Where(x => x.SecurityId == security.Id && x.Interval == "1d" && priceTimes.Contains(x.PriceTime))
                .ToDictionaryAsync(x => x.PriceTime, cancellationToken);

            foreach (var row in rowsBySecurity)
            {
                var priceTime = row.PriceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                if (existingPrices.TryGetValue(priceTime, out var existing))
                {
                    existing.Open = row.Open;
                    existing.High = row.High;
                    existing.Low = row.Low;
                    existing.Close = row.Close;
                    existing.AdjustedClose = row.AdjustedClose;
                    existing.Volume = row.Volume;
                    existing.DataSource = DemoPriceDataSource;
                    updated++;
                    continue;
                }

                _dbContext.MarketPrices.Add(new MarketPrice
                {
                    Id = Guid.NewGuid(),
                    SecurityId = security.Id,
                    PriceTime = priceTime,
                    Interval = "1d",
                    Open = row.Open,
                    High = row.High,
                    Low = row.Low,
                    Close = row.Close,
                    AdjustedClose = row.AdjustedClose,
                    Volume = row.Volume,
                    DataSource = DemoPriceDataSource,
                    CreatedAtUtc = DateTime.UtcNow
                });
                inserted++;
            }
        }

        return new MarketPriceSeedResult(inserted, updated);
    }

    // 從應用程式目錄向上遞迴搜尋並讀取演示價格 CSV 檔案
    private static async Task<IReadOnlyList<DemoPriceCsvRow>> ReadDemoPriceCsvAsync(CancellationToken cancellationToken)
    {
        var csvPath = ResolveDemoPriceCsvPath();
        var rows = new List<DemoPriceCsvRow>();
        using var reader = new StreamReader(csvPath);
        await reader.ReadLineAsync(cancellationToken);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(',');
            rows.Add(new DemoPriceCsvRow(
                columns[0].Trim().ToUpperInvariant(),
                columns[1].Trim().ToUpperInvariant(),
                columns[2].Trim(),
                DateOnly.Parse(columns[3], CultureInfo.InvariantCulture),
                decimal.Parse(columns[5], CultureInfo.InvariantCulture),
                decimal.Parse(columns[6], CultureInfo.InvariantCulture),
                decimal.Parse(columns[7], CultureInfo.InvariantCulture),
                decimal.Parse(columns[8], CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(columns[9]) ? null : decimal.Parse(columns[9], CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(columns[10]) ? null : long.Parse(columns[10], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    // 從應用程式基底目錄向上遞迴搜尋演示價格 CSV 檔案路徑
    private static string ResolveDemoPriceCsvPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, DemoPriceCsvPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Demo market price CSV was not found at '{DemoPriceCsvPath}'.");
    }

    // 將股票代號與交易所組合為唯一鍵字串
    private static string SecurityKey(string ticker, string exchange)
    {
        return $"{ticker.ToUpperInvariant()}|{exchange.ToUpperInvariant()}";
    }

    // 依據股票代號與名稱推斷資產類型：特定 ETF 代號或名稱含 "ETF" 則為 ETF，其餘為 Equity
    private static string InferAssetType(string ticker, string name)
    {
        return ticker is "SPY" or "QQQ" or "GLD" or "TLT" or "IBIT" or "0050" || name.Contains("ETF", StringComparison.OrdinalIgnoreCase)
            ? "ETF"
            : "Equity";
    }

    private sealed record DemoPortfolioDefinition(
        string Name,
        string Description,
        string BaseCurrency,
        IReadOnlyList<DemoHoldingDefinition> Holdings);

    private sealed record DemoHoldingDefinition(
        string Ticker,
        string Exchange,
        decimal Quantity,
        decimal AverageCost,
        string CostCurrency);

    private sealed record DemoPriceCsvRow(
        string Ticker,
        string Exchange,
        string Name,
        DateOnly PriceDate,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal? AdjustedClose,
        long? Volume);

    private sealed record SecuritySeedResult(
        IReadOnlyDictionary<string, Security> SecurityByTickerExchange,
        int SecuritiesCreated);

    private sealed record PortfolioSeedResult(int PortfoliosCreated, int HoldingsCreated);

    private sealed record MarketPriceSeedResult(int Inserted, int Updated);
}
