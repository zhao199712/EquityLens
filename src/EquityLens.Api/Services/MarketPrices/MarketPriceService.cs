using System.Text.Json;
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
    private const string DailyInterval = "1d";

    // 前後還原因子相對差距容忍值；最小的除權息約 0.1% 以上，0.01% 足以吸收 Yahoo 小數進位誤差。
    private const decimal AdjustmentFactorTolerance = 0.0001m;

    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IEnumerable<IMarketDataProvider> _marketDataProviders;
    private readonly ILogger<MarketPriceService> _logger;
    private readonly IOfficialAdjustedCloseRebuilder? _adjustedCloseRebuilder;

    /// <summary>
    /// 初始化市場價格服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="securityRepository">證券儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="marketDataProviders">市場資料提供者集合。</param>
    /// <param name="logger">日誌記錄器。</param>
    /// <param name="adjustedCloseRebuilder">以官方公司行動重建全歷史還原價；未提供時僅使用 Yahoo 回補。</param>
    public MarketPriceService(
        EquityLensDbContext dbContext,
        ISecurityRepository securityRepository,
        IMarketPriceRepository marketPriceRepository,
        IEnumerable<IMarketDataProvider> marketDataProviders,
        ILogger<MarketPriceService> logger,
        IOfficialAdjustedCloseRebuilder? adjustedCloseRebuilder = null)
    {
        _adjustedCloseRebuilder = adjustedCloseRebuilder;
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

        if (!force && IsPricesSyncedToday(security))
        {
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
                0));
        }

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);

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
            value.UpdatedCount));
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
        var from = to.AddDays(-days);

        foreach (var security in candidates)
        {
            if (!force && IsPricesSyncedToday(security))
            {
                skipped++;
                continue;
            }

            try
            {
                var result = await ImportPricesForSecurityAsync(security, from, to, cancellationToken);
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
            catch (Exception ex)
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
            value.UpdatedCount));
    }

    private async Task<Result<ImportMarketPricesResponse>> ImportPricesForSecurityAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
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

            if (!await TryRebuildOfficialAdjustedCloseAsync(security, provider.SourceName, cancellationToken))
            {
                await TryBackfillAdjustedCloseAsync(security, from, to, cancellationToken);
            }

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

    /// <summary>
    /// FinMind 原始價格匯入後，以官方公司行動重建全歷史還原價。
    /// 每次除權息都會改變所有過去日期的還原因子，因此必須重算全部歷史，而非只更新本次同步區間。
    /// 安全檢查未通過或資料來源不可用時回傳 false，改用 Yahoo 回補；失敗不影響價格匯入。
    /// </summary>
    private async Task<bool> TryRebuildOfficialAdjustedCloseAsync(
        Security security,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (_adjustedCloseRebuilder is null || providerName != "FinMind")
        {
            return false;
        }

        try
        {
            var result = await _adjustedCloseRebuilder.RebuildAsync(security, cancellationToken);
            return result.Applied;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Official adjusted-close rebuild failed for {Ticker}; falling back to Yahoo backfill.", security.Ticker);
            return false;
        }
    }

    /// <summary>
    /// 台股（TWSE/TPEX）匯入價格後，透過 Yahoo Finance 回補缺失的調整後收盤價。
    /// 失敗僅記錄 log，不影響匯入結果。
    /// </summary>
    private async Task TryBackfillAdjustedCloseAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (security.Exchange is not ("TWSE" or "TPEX"))
        {
            return;
        }

        var yahoo = _marketDataProviders.FirstOrDefault(x => x.SourceName == "YahooFinance");
        if (yahoo is null)
        {
            return;
        }

        try
        {
            var yahooPrices = await yahoo.GetDailyPricesAsync(security, from, to, cancellationToken);
            var adjustedCloseByDate = yahooPrices
                .Where(x => x.AdjustedClose.HasValue)
                .GroupBy(x => x.Date)
                .ToDictionary(x => x.Key, x => x.First().AdjustedClose!.Value);
            if (adjustedCloseByDate.Count == 0)
            {
                return;
            }

            await _marketPriceRepository.UpdateAdjustedCloseAsync(
                security.Id, adjustedCloseByDate, cancellationToken);

            await FillAdjustedCloseGapsAsync(security, from, to, adjustedCloseByDate.Keys.ToHashSet(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Adjusted-close backfill failed for {Ticker} ({Exchange}).", security.Ticker, security.Exchange);
        }
    }

    /// <summary>
    /// 補齊或校正 Yahoo 這次沒有提供的單日還原收盤價。
    /// 僅在該日前後兩個交易日的還原因子（還原價 ÷ 收盤價）一致時，才以該因子乘上當日收盤價推算：
    /// 該日為空值時補值；該日保留了舊值但因子與前後不一致（之後有新的除權息、舊因子已過期）時重算。
    /// 前後因子不一致代表期間可能有除權息，維持原狀，不以原始收盤價代替。
    /// </summary>
    private async Task FillAdjustedCloseGapsAsync(
        Security security,
        DateOnly from,
        DateOnly to,
        IReadOnlySet<DateOnly> providerDates,
        CancellationToken cancellationToken)
    {
        var rows = (await _marketPriceRepository.GetBySecurityAsync(security.Id, from, to, cancellationToken))
            .Where(x => x.Interval == DailyInterval)
            .OrderBy(x => x.PriceTime)
            .ToList();

        var filled = new Dictionary<DateOnly, decimal>();
        for (var i = 1; i < rows.Count - 1; i++)
        {
            var current = rows[i];
            var date = DateOnly.FromDateTime(current.PriceTime);
            if (providerDates.Contains(date) || current.Close <= 0)
            {
                continue;
            }

            var previousFactor = AdjustmentFactor(rows[i - 1]);
            var nextFactor = AdjustmentFactor(rows[i + 1]);
            if (previousFactor is null || nextFactor is null ||
                Math.Abs(previousFactor.Value - nextFactor.Value) > nextFactor.Value * AdjustmentFactorTolerance)
            {
                continue;
            }

            var currentFactor = AdjustmentFactor(current);
            if (currentFactor is not null &&
                Math.Abs(currentFactor.Value - previousFactor.Value) <= previousFactor.Value * AdjustmentFactorTolerance)
            {
                continue;
            }

            filled[date] = Math.Round(current.Close * previousFactor.Value, 6);
        }

        if (filled.Count > 0)
        {
            await _marketPriceRepository.UpdateAdjustedCloseAsync(security.Id, filled, cancellationToken);
            _logger.LogInformation(
                "Filled or repaired {Count} adjusted-close value(s) for {Ticker} ({Exchange}) from neighbouring adjustment factors.",
                filled.Count, security.Ticker, security.Exchange);
        }
    }

    private static decimal? AdjustmentFactor(MarketPriceResponse price) =>
        price.AdjustedClose is { } adjusted && adjusted > 0 && price.Close > 0
            ? adjusted / price.Close
            : null;

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

    private static bool IsPricesSyncedToday(Security security)
    {
        if (security.PricesSyncedAtUtc is null)
        {
            return false;
        }

        var syncedDate = DateOnly.FromDateTime(security.PricesSyncedAtUtc.Value);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return syncedDate == today;
    }
}
