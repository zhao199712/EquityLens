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

    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;
    private readonly IMarketPriceRepository _marketPriceRepository;
    private readonly IEnumerable<IMarketDataProvider> _marketDataProviders;

    /// <summary>
    /// 初始化市場價格服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="securityRepository">證券儲存庫。</param>
    /// <param name="marketPriceRepository">市場價格儲存庫。</param>
    /// <param name="marketDataProviders">市場資料提供者集合。</param>
    public MarketPriceService(
        EquityLensDbContext dbContext,
        ISecurityRepository securityRepository,
        IMarketPriceRepository marketPriceRepository,
        IEnumerable<IMarketDataProvider> marketDataProviders)
    {
        _dbContext = dbContext;
        _securityRepository = securityRepository;
        _marketPriceRepository = marketPriceRepository;
        _marketDataProviders = marketDataProviders;
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
