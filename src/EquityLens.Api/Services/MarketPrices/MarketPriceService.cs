using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>
/// 市場價格服務實現，提供證券價格查詢、每日價格導入與同步功能。
/// </summary>
public sealed class MarketPriceService : IMarketPriceService
{
    private readonly TaiwanPriceImportCoordinator? _taiwanImporter;
    private const string DailyInterval = "1d";

    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IEnumerable<IMarketDataProvider> _marketDataProviders;
    private readonly ILogger<MarketPriceService> _logger;

    /// <summary>
    /// 初始化市場價格服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="securityRepository">證券儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="marketDataProviders">市場資料提供者集合。</param>
    /// <param name="logger">日誌記錄器。</param>
    public MarketPriceService(
        EquityLensDbContext dbContext,
        ISecurityRepository securityRepository,
        IMarketPriceRepository marketPriceRepository,
        IEnumerable<IMarketDataProvider> marketDataProviders,
        ILogger<MarketPriceService> logger,
        TaiwanPriceImportCoordinator? taiwanImporter = null)
    {
        _taiwanImporter = taiwanImporter;
        _dbContext = dbContext;
        _securityRepository = securityRepository;
        _marketPriceRepository = marketPriceRepository;
        _marketDataProviders = marketDataProviders;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MarketPriceResponse>>> GetPricesAsync(
        Guid securityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            return Result<IReadOnlyList<MarketPriceResponse>>.Failure("market_price.invalid_range", "From date must be before or equal to to date.");
        }

        if (!await _securityRepository.ActiveExistsAsync(securityId, cancellationToken))
        {
            return Result<IReadOnlyList<MarketPriceResponse>>.Failure("security.not_found", "Security was not found.");
        }

        var prices = await _marketPriceRepository.GetBySecurityAsync(securityId, from, to, cancellationToken);
        return Result<IReadOnlyList<MarketPriceResponse>>.Success(prices);
    }

    /// <inheritdoc />
    public async Task<Result<ImportMarketPricesResponse>> ImportDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            return Result<ImportMarketPricesResponse>.Failure("market_price.invalid_range", "From date must be before or equal to to date.");
        }

        var security = await _securityRepository.GetEntityAsync(securityId, cancellationToken);
        if (security is null)
        {
            return Result<ImportMarketPricesResponse>.Failure("security.not_found", "Security was not found.");
        }

        return await ImportPricesForSecurityAsync(security, request.From, request.To, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<SyncMarketPricesResponse>> SyncDailyPricesAsync(
        Guid securityId,
        int days,
        bool force,
        CancellationToken cancellationToken)
    {
        var security = await _securityRepository.GetEntityAsync(securityId, cancellationToken);
        if (security is null)
        {
            return Result<SyncMarketPricesResponse>.Failure("security.not_found", "Security was not found.");
        }

        var to = security.Exchange is "TWSE" or "TPEX"
            ? TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.UtcNow)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);

        if (!force && await IsPricesSyncedTodayAsync(security, from, to, cancellationToken))
        {
            var skipBatch = security.PriceAdjustmentBatchId is null ? null :
                await _dbContext.PriceAdjustmentBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == security.PriceAdjustmentBatchId, cancellationToken);
            return Result<SyncMarketPricesResponse>.Success(new SyncMarketPricesResponse(
                securityId,
                security.PricesSource,
                false,
                true,
                "Prices are already synced today.",
                null,
                null,
                0,
                0,
                0, skipBatch?.Source, skipBatch?.Id, skipBatch?.From, skipBatch?.To, skipBatch?.VerifiedThrough));
        }

        var importResult = await ImportPricesForSecurityAsync(security, from, to, cancellationToken);
        if (!importResult.IsSuccess)
        {
            return Result<SyncMarketPricesResponse>.Failure(importResult.ErrorCode!, importResult.ErrorMessage!);
        }

        var value = importResult.Value!;
        return Result<SyncMarketPricesResponse>.Success(new SyncMarketPricesResponse(
            securityId,
            value.Source,
            true,
            false,
            null,
            from,
            to,
            value.ImportedCount,
            value.InsertedCount,
            value.UpdatedCount, value.AdjustmentSource, value.AdjustmentVersion,
            value.CoverageFrom, value.CoverageTo, value.VerifiedThrough));
    }

    /// <inheritdoc />
    public async Task<RefreshSecuritiesPricesResponse> RefreshAllPricesAsync(
        int days,
        bool force,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _securityRepository.GetActiveEntitiesAsync(limit, cancellationToken);

        var synced = 0;
        var skipped = 0;
        var failed = 0;
        var totalPricesImported = 0;
        var failures = new List<RefreshSecurityPriceFailureResponse>();

        var to = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var security in candidates)
        {
            var securityTo = security.Exchange is "TWSE" or "TPEX"
                ? TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.UtcNow) : to;
            if (!force && await IsPricesSyncedTodayAsync(security, securityTo.AddDays(-days), securityTo, cancellationToken))
            {
                skipped++;
                continue;
            }

            try
            {
                var result = await ImportPricesForSecurityAsync(security, securityTo.AddDays(-days), securityTo, cancellationToken);
                if (result.IsSuccess)
                {
                    synced++;
                    totalPricesImported += result.Value!.ImportedCount;
                }
                else
                {
                    failed++;
                    failures.Add(new RefreshSecurityPriceFailureResponse(
                        security.Id,
                        security.Ticker,
                        security.Exchange,
                        result.ErrorMessage ?? "Unknown error"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                failures.Add(new RefreshSecurityPriceFailureResponse(
                    security.Id,
                    security.Ticker,
                    security.Exchange,
                    ex.Message));
            }

            // 每支證券之間延遲 1000ms，避免 provider 被限流
            if (candidates.Count > 1)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        return new RefreshSecuritiesPricesResponse(
            candidates.Count,
            candidates.Count,
            synced,
            skipped,
            failed,
            totalPricesImported,
            failures);
    }

    /// <inheritdoc />
    public async Task<Result<ImportMarketPricesByTickerResponse>> ImportDailyPricesByTickerAsync(
        ImportMarketPricesByTickerRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Exchange))
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("security.required_fields", "Ticker and exchange are required.");
        }

        if (request.From > request.To)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("market_price.invalid_range", "From date must be before or equal to to date.");
        }

        var ticker = request.Ticker.Trim().ToUpperInvariant();
        var exchange = request.Exchange.Trim().ToUpperInvariant();

        var security = await _securityRepository.GetEntityByTickerExchangeAsync(ticker, exchange, cancellationToken);
        if (security is null)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("security.not_found", "Security was not found.");
        }

        var importResult = await ImportPricesForSecurityAsync(security, request.From, request.To, cancellationToken);
        if (!importResult.IsSuccess)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure(importResult.ErrorCode!, importResult.ErrorMessage!);
        }

        var value = importResult.Value!;
        return Result<ImportMarketPricesByTickerResponse>.Success(new ImportMarketPricesByTickerResponse(
            security.Id,
            false,
            security.Ticker,
            security.Exchange,
            value.Source,
            value.ImportedCount,
            value.InsertedCount,
            value.UpdatedCount, value.AdjustmentSource, value.AdjustmentVersion,
            value.CoverageFrom, value.CoverageTo, value.VerifiedThrough));
    }

    private async Task<Result<ImportMarketPricesResponse>> ImportPricesForSecurityAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (security.Exchange is "TWSE" or "TPEX")
        {
            if (_taiwanImporter is null)
                return Result<ImportMarketPricesResponse>.Failure("market_price.provider_error", "Verified Taiwan importer is unavailable.");
            return await _taiwanImporter.ImportAsync(security.Id, from, to, cancellationToken);
        }

        var providers = GetOrderedPriceProviders(security.Exchange);
        if (providers.Count == 0)
        {
            return Result<ImportMarketPricesResponse>.Failure(
                "market_price.unsupported_exchange",
                $"Exchange '{security.Exchange}' is not supported for market data import.");
        }

        var failureReasons = new List<string>();

        foreach (var provider in providers)
        {
            IReadOnlyList<ImportedMarketPrice> importedPrices;
            try
            {
                importedPrices = await provider.GetDailyPricesAsync(security, from, to, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                failureReasons.Add($"{provider.SourceName}: {ex.Message}");
                continue;
            }
            catch (HttpRequestException ex)
            {
                failureReasons.Add($"{provider.SourceName}: {ex.Message}");
                continue;
            }
            catch (JsonException ex)
            {
                failureReasons.Add($"{provider.SourceName}: {ex.Message}");
                continue;
            }

            if (importedPrices.Count == 0)
            {
                failureReasons.Add($"{provider.SourceName}: returned 0 prices");
                continue;
            }

            var marketPrices = importedPrices
                .GroupBy(x => x.Date)
                .Select(x => x.First())
                .Select(x => new MarketPrice
                {
                    SecurityId = security.Id,
                    PriceTime = x.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Interval = DailyInterval,
                    Open = x.Open,
                    High = x.High,
                    Low = x.Low,
                    Close = x.Close,
                    AdjustedClose = x.AdjustedClose,
                    Volume = x.Volume,
                    DataSource = provider.SourceName
                })
                .ToList();

            var upsertResult = await _marketPriceRepository.UpsertDailyPricesAsync(marketPrices, cancellationToken);

            security.PricesSyncedAtUtc = DateTime.UtcNow;
            security.PricesSource = provider.SourceName;

            await _dbContext.SaveChangesAsync(cancellationToken);


            return Result<ImportMarketPricesResponse>.Success(new ImportMarketPricesResponse(
                security.Id,
                provider.SourceName,
                marketPrices.Count,
                upsertResult.InsertedCount,
                upsertResult.UpdatedCount));
        }

        var errorDetail = string.Join("; ", failureReasons);
        return Result<ImportMarketPricesResponse>.Failure(
            "market_price.provider_error",
            $"No price data returned from any provider. Details: {errorDetail}");
    }

    private IReadOnlyList<IMarketDataProvider> GetOrderedPriceProviders(string exchange)
    {
        var normalized = exchange.Trim().ToUpperInvariant();
        var allProviders = _marketDataProviders.ToList();

        var yahoo = allProviders.FirstOrDefault(x => x.SourceName == "YahooFinance" && x.Supports(normalized));
        var alpha = allProviders.FirstOrDefault(x => x.SourceName == "AlphaVantage" && x.Supports(normalized));
        var finmind = allProviders.FirstOrDefault(x => x.SourceName == "FinMind" && x.Supports(normalized));

        var ordered = new List<IMarketDataProvider>();

        switch (normalized)
        {
            case "NASDAQ":
            case "NYSE":
            case "AMEX":
            case "US":
                if (yahoo != null) ordered.Add(yahoo);
                if (alpha != null) ordered.Add(alpha);
                break;

            case "TWSE":
            case "TPEX":
                if (finmind != null) ordered.Add(finmind);
                break;
        }

        return ordered;
    }

    private async Task<bool> IsPricesSyncedTodayAsync(Security security, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (security.PricesSyncedAtUtc is null ||
            DateOnly.FromDateTime(security.PricesSyncedAtUtc.Value) != DateOnly.FromDateTime(DateTime.UtcNow))
            return false;
        if (security.Exchange is not ("TWSE" or "TPEX")) return true;
        if (security.PriceAdjustmentBatchId is null || security.PricesVerifiedThrough is null) return false;
        var batch = await _dbContext.PriceAdjustmentBatches.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == security.PriceAdjustmentBatchId && x.SecurityId == security.Id, ct);
        if (batch is null || batch.From > from || batch.To < to || batch.VerifiedThrough != security.PricesVerifiedThrough)
            return false;
        if (batch.VerifiedThrough < batch.From || batch.VerifiedThrough > batch.To) return false;
        // 核對整個批次的交易日與明確無成交證據；不能只檢查剩餘價格列。
        var calendarDates = new HashSet<DateOnly>();
        var noTrades = new HashSet<DateOnly>();
        try
        {
            using var snapshot = JsonDocument.Parse(batch.SnapshotJson);
            if (snapshot.RootElement.TryGetProperty("noTrades", out var gaps))
                noTrades = gaps.EnumerateArray().Select(x => DateOnly.Parse(x.GetString()!)).ToHashSet();
            using var sources = JsonDocument.Parse(snapshot.RootElement.GetProperty("official").GetString()!);
            foreach (var source in sources.RootElement.EnumerateArray())
            {
                if (source.GetProperty("source").GetString() != "FinMind:TaiwanStockTradingDate") continue;
                using var calendar = JsonDocument.Parse(source.GetProperty("snapshot").GetString()!);
                var dates = calendar.RootElement.GetProperty("data").EnumerateArray()
                    .Select(x => DateOnly.Parse(x.GetProperty("date").GetString()!)).ToList();
                calendarDates = dates.ToHashSet();
                if (calendarDates.Count != dates.Count) return false;
                break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { return false; }
        var lastRequired = calendarDates.Where(x => x >= from && x <= to).Select(x => (DateOnly?)x).Max();
        if (lastRequired is null || batch.VerifiedThrough < lastRequired) return false;
        var expectedDates = calendarDates.Where(x => x >= batch.From && x <= batch.VerifiedThrough).ToHashSet();
        if (!noTrades.IsSubsetOf(expectedDates)) return false;
        expectedDates.ExceptWith(noTrades);
        var prices = await _dbContext.MarketPrices.AsNoTracking()
            .Where(x => x.SecurityId == security.Id && x.Interval == "1d")
            .Select(x => new { x.PriceTime, x.PriceAdjustmentBatchId, x.AdjustedClose, x.Open, x.High, x.Low, x.Close })
            .ToListAsync(ct);
        if (prices.Count == 0 || prices.Any(x => x.PriceAdjustmentBatchId != batch.Id || x.AdjustedClose is null or <= 0 ||
                x.Open <= 0 || x.High <= 0 || x.Low <= 0 || x.Close <= 0)) return false;
        var actualDates = prices.Select(x => DateOnly.FromDateTime(x.PriceTime)).ToHashSet();
        return actualDates.Count == prices.Count && actualDates.SetEquals(expectedDates);
    }
}
