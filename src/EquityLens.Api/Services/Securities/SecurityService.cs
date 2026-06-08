using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.Redis;

namespace EquityLens.Api.Services.Securities;

/// <summary>
/// 證券資料服務實現，提供證券查詢、搜尋、建立與解析功能。
/// </summary>
public sealed class SecurityService : ISecurityService
{
    private static readonly TimeSpan MetadataStaleThreshold = TimeSpan.FromDays(7);

    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;
    private readonly IEnumerable<IMarketDataProvider> _marketDataProviders;
    private readonly IRedisCacheService _redisCache;

    /// <summary>
    /// 初始化證券服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="securityRepository">證券儲存庫。</param>
    /// <param name="marketDataProviders">市場資料提供者集合。</param>
    /// <param name="redisCache">Redis 快取服務。</param>
    public SecurityService(
        EquityLensDbContext dbContext,
        ISecurityRepository securityRepository,
        IEnumerable<IMarketDataProvider> marketDataProviders,
        IRedisCacheService redisCache)
    {
        _dbContext = dbContext;
        _securityRepository = securityRepository;
        _marketDataProviders = marketDataProviders;
        _redisCache = redisCache;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        return _securityRepository.SearchAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecuritySearchResult>> SearchAvailableAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = query.Trim();
        var cacheKey = $"equitylens:cache:security-search:{normalizedQuery}";

        // 嘗試從 Redis 快取取得
        var cached = await _redisCache.GetAsync<IReadOnlyList<SecuritySearchResult>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // 先查詢本地資料庫中的證券
        var localResults = (await _securityRepository.SearchAsync(normalizedQuery, cancellationToken))
            .Select(x => new SecuritySearchResult(
                x.Id,
                x.Ticker,
                x.Exchange,
                x.Name,
                x.AssetType,
                x.Currency,
                x.Isin,
                x.Sector,
                x.Industry,
                "Local"))
            .ToList();

        var results = new List<SecuritySearchResult>(localResults);

        // 再依序查詢各外部資料提供者，並合併結果
        foreach (var provider in _marketDataProviders)
        {
            IReadOnlyList<ExternalSecuritySearchResult> externalResults;
            try
            {
                externalResults = await provider.SearchSecuritiesAsync(normalizedQuery, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (HttpRequestException)
            {
                continue;
            }
            catch (JsonException)
            {
                continue;
            }

            results.AddRange(externalResults.Select(x => new SecuritySearchResult(
                null,
                x.Ticker,
                x.Exchange,
                x.Name,
                x.AssetType,
                x.Currency,
                x.Isin,
                x.Sector,
                x.Industry,
                x.Source)));
        }

        var finalResults = results
            .GroupBy(x => new { x.Ticker, x.Exchange })
            .Select(x => x.OrderByDescending(result => result.SecurityId.HasValue).First())
            .OrderBy(x => x.Ticker)
            .ThenBy(x => x.Exchange)
            .Take(25)
            .ToList();

        // 寫入 Redis 快取
        await _redisCache.SetAsync(cacheKey, finalResults, cancellationToken: cancellationToken);

        return finalResults;
    }

    /// <inheritdoc />
    public async Task<Result<SecurityResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var security = await _securityRepository.GetAsync(id, cancellationToken);
        return security is null
            ? Result<SecurityResponse>.Failure("security.not_found", "Security was not found.")
            : Result<SecurityResponse>.Success(security);
    }

    /// <inheritdoc />
    public async Task<Result<SecurityResponse>> CreateAsync(
        CreateSecurityRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Ticker) ||
            string.IsNullOrWhiteSpace(request.Exchange) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<SecurityResponse>.Failure("security.required_fields", "Ticker, exchange, and name are required.");
        }

        var ticker = request.Ticker.Trim().ToUpperInvariant();
        var exchange = request.Exchange.Trim().ToUpperInvariant();

        if (await _securityRepository.TickerExchangeExistsAsync(ticker, exchange, cancellationToken))
        {
            return Result<SecurityResponse>.Failure("security.duplicate", "Security already exists for ticker and exchange.");
        }

        var security = new Security
        {
            Ticker = ticker,
            Exchange = exchange,
            Name = request.Name.Trim(),
            AssetType = string.IsNullOrWhiteSpace(request.AssetType) ? null : request.AssetType.Trim(),
            Currency = NormalizeCurrency(request.Currency),
            Isin = string.IsNullOrWhiteSpace(request.Isin) ? null : request.Isin.Trim().ToUpperInvariant(),
            Sector = string.IsNullOrWhiteSpace(request.Sector) ? null : request.Sector.Trim(),
            Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim(),
            IsActive = true,
            MetadataUpdatedAtUtc = DateTime.UtcNow,
            MetadataSource = "Manual"
        };

        _securityRepository.Add(security);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _securityRepository.GetAsync(security.Id, cancellationToken);
        return Result<SecurityResponse>.Success(response!);
    }

    /// <inheritdoc />
    public async Task<Result<Security>> EnsureAsync(
        EnsureSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await EnsureCoreAsync(request, cancellationToken);
        return result.IsSuccess
            ? Result<Security>.Success(result.Value!.Security)
            : Result<Security>.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    /// <inheritdoc />
    public async Task<Result<ResolveSecurityResponse>> ResolveAsync(
        ResolveSecurityRequest request,
        CancellationToken cancellationToken)
    {
        Security? security = null;

        // 嘗試從 SecurityId 或 Ticker+Exchange 取得本地 Security
        if (request.SecurityId.HasValue)
        {
            security = await _securityRepository.GetEntityAsync(request.SecurityId.Value, cancellationToken);
            if (security is null)
            {
                return Result<ResolveSecurityResponse>.Failure("security.not_found", "Security was not found.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Ticker) && !string.IsNullOrWhiteSpace(request.Exchange))
        {
            var ticker = request.Ticker.Trim().ToUpperInvariant();
            var exchange = request.Exchange.Trim().ToUpperInvariant();
            security = await _securityRepository.GetEntityByTickerExchangeAsync(ticker, exchange, cancellationToken);
        }
        else
        {
            return Result<ResolveSecurityResponse>.Failure("security.required_fields", "SecurityId or ticker and exchange are required.");
        }

        if (security is not null)
        {
            // 本地已有：檢查 metadata 是否過期，過期則嘗試刷新
            if (IsMetadataStale(security))
            {
                await TryRefreshMetadataAsync(security, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result<ResolveSecurityResponse>.Success(ToResolveResponse(security, false));
        }

        // 本地沒有：嘗試從外部 API 建立
        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Exchange))
        {
            return Result<ResolveSecurityResponse>.Failure("security.required_fields", "Ticker and exchange are required to create a new security.");
        }

        var newTicker = request.Ticker.Trim().ToUpperInvariant();
        var newExchange = request.Exchange.Trim().ToUpperInvariant();

        security = await TryCreateFromExternalAsync(newTicker, newExchange, request, cancellationToken);
        if (security is null)
        {
            return Result<ResolveSecurityResponse>.Failure("security.not_found", "Security was not found in external providers.");
        }

        _securityRepository.Add(security);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 清除搜尋快取
        await _redisCache.RemoveByPatternAsync("equitylens:cache:security-search:*");

        return Result<ResolveSecurityResponse>.Success(ToResolveResponse(security, true));
    }

    // 確保證券存在的核心邏輯：依 SecurityId 查詢，或依代號/交易所查詢/建立
    private async Task<Result<EnsureSecurityResult>> EnsureCoreAsync(
        EnsureSecurityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SecurityId is not null)
        {
            var existing = await _securityRepository.GetEntityAsync(request.SecurityId.Value, cancellationToken);
            return existing is null
                ? Result<EnsureSecurityResult>.Failure("security.not_found", "Security was not found.")
                : Result<EnsureSecurityResult>.Success(new EnsureSecurityResult(existing, false));
        }

        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Exchange))
        {
            return Result<EnsureSecurityResult>.Failure("security.required_fields", "SecurityId or ticker and exchange are required.");
        }

        var ticker = request.Ticker.Trim().ToUpperInvariant();
        var exchange = request.Exchange.Trim().ToUpperInvariant();
        var security = await _securityRepository.GetEntityByTickerExchangeAsync(ticker, exchange, cancellationToken);
        if (security is not null)
        {
            return Result<EnsureSecurityResult>.Success(new EnsureSecurityResult(security, false));
        }

        security = new Security
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Exchange = exchange,
            Name = string.IsNullOrWhiteSpace(request.Name) ? ticker : request.Name.Trim(),
            AssetType = string.IsNullOrWhiteSpace(request.AssetType) ? null : request.AssetType.Trim(),
            Currency = NormalizeCurrency(request.Currency),
            Isin = string.IsNullOrWhiteSpace(request.Isin) ? null : request.Isin.Trim().ToUpperInvariant(),
            Sector = string.IsNullOrWhiteSpace(request.Sector) ? null : request.Sector.Trim(),
            Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim(),
            IsActive = true,
            MetadataUpdatedAtUtc = DateTime.UtcNow,
            MetadataSource = "Manual"
        };

        _securityRepository.Add(security);
        return Result<EnsureSecurityResult>.Success(new EnsureSecurityResult(security, true));
    }

    // 判斷證券 metadata 是否已過期（超過 7 天未更新）
    private static bool IsMetadataStale(Security security)
    {
        if (security.MetadataUpdatedAtUtc is null)
        {
            return true;
        }

        return DateTime.UtcNow - security.MetadataUpdatedAtUtc.Value > MetadataStaleThreshold;
    }

    // 嘗試從外部 API 刷新證券 metadata；失敗則靜默忽略
    private async Task TryRefreshMetadataAsync(Security security, CancellationToken cancellationToken)
    {
        var provider = _marketDataProviders.FirstOrDefault(x => x.Supports(security.Exchange));
        if (provider is null)
        {
            return;
        }

        try
        {
            var external = await provider.ResolveSecurityAsync(security.Ticker, security.Exchange, cancellationToken);
            if (external is null)
            {
                return;
            }

            // 保守更新：只覆蓋 Name 與 Currency；其餘欄位僅在本地為 null 時補上
            security.Name = external.Name;
            security.AssetType ??= external.AssetType;
            security.Currency = string.IsNullOrWhiteSpace(external.Currency)
                ? security.Currency
                : external.Currency.Trim().ToUpperInvariant();
            security.Isin ??= external.Isin;
            security.Sector ??= external.Sector;
            security.Industry ??= external.Industry;
            security.MetadataUpdatedAtUtc = DateTime.UtcNow;
            security.MetadataSource = provider.SourceName;
        }
        catch
        {
            // 外部 API 錯誤時靜默忽略，不影響既有資料
        }
    }

    // 嘗試從外部 API 建立證券；失敗時若 request 有 Name 則回退到 request 資料
    private async Task<Security?> TryCreateFromExternalAsync(
        string ticker,
        string exchange,
        ResolveSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _marketDataProviders.FirstOrDefault(x => x.Supports(exchange));
        if (provider is not null)
        {
            try
            {
                var external = await provider.ResolveSecurityAsync(ticker, exchange, cancellationToken);
                if (external is not null)
                {
                    return new Security
                    {
                        Id = Guid.NewGuid(),
                        Ticker = ticker,
                        Exchange = exchange,
                        Name = external.Name,
                        AssetType = external.AssetType,
                        Currency = string.IsNullOrWhiteSpace(external.Currency)
                            ? NormalizeCurrency(request.Currency)
                            : external.Currency.Trim().ToUpperInvariant(),
                        Isin = external.Isin,
                        Sector = external.Sector,
                        Industry = external.Industry,
                        IsActive = true,
                        MetadataUpdatedAtUtc = DateTime.UtcNow,
                        MetadataSource = provider.SourceName
                    };
                }
            }
            catch
            {
                // 外部 API 錯誤，回退到 request 資料
            }
        }

        // 外部 API 找不到或失敗，嘗試用 request 資料建立
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            return new Security
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Exchange = exchange,
                Name = request.Name.Trim(),
                AssetType = string.IsNullOrWhiteSpace(request.AssetType) ? null : request.AssetType.Trim(),
                Currency = NormalizeCurrency(request.Currency),
                Isin = string.IsNullOrWhiteSpace(request.Isin) ? null : request.Isin.Trim().ToUpperInvariant(),
                Sector = string.IsNullOrWhiteSpace(request.Sector) ? null : request.Sector.Trim(),
                Industry = string.IsNullOrWhiteSpace(request.Industry) ? null : request.Industry.Trim(),
                IsActive = true,
                MetadataUpdatedAtUtc = DateTime.UtcNow,
                MetadataSource = "Manual"
            };
        }

        return null;
    }

    // 將 Security 實體轉換為 ResolveSecurityResponse
    private static ResolveSecurityResponse ToResolveResponse(Security security, bool created)
    {
        return new ResolveSecurityResponse(
            security.Id,
            created,
            security.Ticker,
            security.Exchange,
            security.Name,
            security.AssetType,
            security.Currency,
            security.Isin,
            security.Sector,
            security.Industry);
    }

    // 將貨幣代碼標準化：空白時預設為 USD，否則轉為大寫
    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }

    // 確保證券結果的內部記錄，包含證券實體與是否為新建立
    private sealed record EnsureSecurityResult(Security Security, bool Created);
}
