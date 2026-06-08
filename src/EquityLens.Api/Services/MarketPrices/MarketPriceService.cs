using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.MarketPrices;

/// <summary>
/// 市場價格服務實現，提供證券價格查詢與每日價格導入功能。
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

        var provider = _marketDataProviders.FirstOrDefault(x => x.Supports(security.Exchange));
        if (provider is null)
        {
            return Result<ImportMarketPricesResponse>.Failure(
                "market_price.unsupported_exchange",
                $"Exchange '{security.Exchange}' is not supported for market data import.");
        }

        IReadOnlyList<ImportedMarketPrice> importedPrices;
        try
        {
            importedPrices = await provider.GetDailyPricesAsync(security, request.From, request.To, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ImportMarketPricesResponse>.Failure("market_price.provider_not_configured", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return Result<ImportMarketPricesResponse>.Failure("market_price.provider_error", ex.Message);
        }
        catch (JsonException ex)
        {
            return Result<ImportMarketPricesResponse>.Failure("market_price.provider_response_invalid", ex.Message);
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

    /// <inheritdoc />
    public Task<Result<ImportMarketPricesResponse>> SyncDailyPricesAsync(
        Guid securityId,
        ImportMarketPricesRequest request,
        CancellationToken cancellationToken)
    {
        return ImportDailyPricesAsync(securityId, request, cancellationToken);
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

        var securityCreated = false;
        var security = await _securityRepository.GetEntityByTickerExchangeAsync(ticker, exchange, cancellationToken);
        if (security is null)
        {
            security = new Security
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Exchange = exchange,
                Name = string.IsNullOrWhiteSpace(request.Name) ? ticker : request.Name.Trim(),
                AssetType = string.IsNullOrWhiteSpace(request.AssetType) ? null : request.AssetType.Trim(),
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? DefaultCurrency(exchange) : request.Currency.Trim().ToUpperInvariant(),
                Isin = string.IsNullOrWhiteSpace(request.Isin) ? null : request.Isin.Trim().ToUpperInvariant(),
                Sector = string.IsNullOrWhiteSpace(request.Sector) ? null : request.Sector.Trim(),
                Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim(),
                IsActive = true
            };

            _securityRepository.Add(security);
            securityCreated = true;
        }

        var provider = _marketDataProviders.FirstOrDefault(x => x.Supports(security.Exchange));
        if (provider is null)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure(
                "market_price.unsupported_exchange",
                $"Exchange '{security.Exchange}' is not supported for market data import.");
        }

        IReadOnlyList<ImportedMarketPrice> importedPrices;
        try
        {
            importedPrices = await provider.GetDailyPricesAsync(security, request.From, request.To, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("market_price.provider_not_configured", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("market_price.provider_error", ex.Message);
        }
        catch (JsonException ex)
        {
            return Result<ImportMarketPricesByTickerResponse>.Failure("market_price.provider_response_invalid", ex.Message);
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

        return Result<ImportMarketPricesByTickerResponse>.Success(new ImportMarketPricesByTickerResponse(
            security.Id,
            securityCreated,
            security.Ticker,
            security.Exchange,
            provider.SourceName,
            marketPrices.Count,
            upsertResult.InsertedCount,
            upsertResult.UpdatedCount));
    }

    // 依據交易所推斷預設貨幣：TWSE/TPEX 為 TWD，其餘為 USD
    private static string DefaultCurrency(string exchange)
    {
        return exchange is "TWSE" or "TPEX" ? "TWD" : "USD";
    }
}
