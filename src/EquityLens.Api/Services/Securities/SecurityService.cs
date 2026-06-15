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
/// 證券資料服務實現，提供證券查詢、搜尋、解析與刷新功能。
/// 所有證券資料以外部 API 為唯一事實來源。
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

        var cached = await _redisCache.GetAsync<IReadOnlyList<SecuritySearchResult>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

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
            if (IsMetadataStale(security))
            {
                await TryRefreshMetadataAsync(security, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result<ResolveSecurityResponse>.Success(ToResolveResponse(security, false));
        }

        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Exchange))
        {
            return Result<ResolveSecurityResponse>.Failure("security.required_fields", "Ticker and exchange are required to create a new security.");
        }

        var newTicker = request.Ticker.Trim().ToUpperInvariant();
        var newExchange = request.Exchange.Trim().ToUpperInvariant();

        security = await TryCreateFromExternalAsync(newTicker, newExchange, cancellationToken);
        if (security is null)
        {
            return Result<ResolveSecurityResponse>.Failure("security.not_found", "Security was not found in external providers.");
        }

        _securityRepository.Add(security);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _redisCache.RemoveByPatternAsync("equitylens:cache:security-search:*");

        return Result<ResolveSecurityResponse>.Success(ToResolveResponse(security, true));
    }

    /// <inheritdoc />
    public async Task<RefreshSecuritiesResponse> RefreshAllAsync(
        bool force,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await _securityRepository.GetActiveEntitiesAsync(limit, cancellationToken);

        var updated = 0;
        var skipped = 0;
        var failures = new List<RefreshSecurityFailureResponse>();

        foreach (var security in candidates)
        {
            if (!force && !IsMetadataStale(security))
            {
                skipped++;
                continue;
            }

            try
            {
                var refreshed = await TryRefreshMetadataAsync(security, cancellationToken);
                if (refreshed)
                {
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                failures.Add(new RefreshSecurityFailureResponse(
                    security.Id,
                    security.Ticker,
                    security.Exchange,
                    ex.Message));
            }
        }

        if (updated > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new RefreshSecuritiesResponse(
            candidates.Count,
            candidates.Count,
            updated,
            skipped,
            failures.Count,
            failures);
    }

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

        security = await TryCreateFromExternalAsync(ticker, exchange, cancellationToken);
        if (security is null)
        {
            return Result<EnsureSecurityResult>.Failure("security.not_found", "Security was not found in external providers.");
        }

        _securityRepository.Add(security);
        return Result<EnsureSecurityResult>.Success(new EnsureSecurityResult(security, true));
    }

    private static bool IsMetadataStale(Security security)
    {
        if (security.MetadataUpdatedAtUtc is null)
        {
            return true;
        }

        return DateTime.UtcNow - security.MetadataUpdatedAtUtc.Value > MetadataStaleThreshold;
    }

    private async Task<bool> TryRefreshMetadataAsync(Security security, CancellationToken cancellationToken)
    {
        foreach (var provider in _marketDataProviders.Where(x => x.Supports(security.Exchange)))
        {
            try
            {
                var external = await provider.ResolveSecurityAsync(security.Ticker, security.Exchange, cancellationToken);
                if (external is null)
                {
                    continue;
                }

                ApplyExternalMetadata(security, external, provider.SourceName);
                return true;
            }
            catch
            {
                continue;
            }
        }

        return false;
    }

    private async Task<Security?> TryCreateFromExternalAsync(
        string ticker,
        string exchange,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _marketDataProviders.Where(x => x.Supports(exchange)))
        {
            try
            {
                var external = await provider.ResolveSecurityAsync(ticker, exchange, cancellationToken);
                if (external is null)
                {
                    continue;
                }

                var security = new Security
                {
                    Id = Guid.NewGuid(),
                    Ticker = ticker,
                    Exchange = exchange,
                    IsActive = true,
                    MetadataUpdatedAtUtc = DateTime.UtcNow,
                    MetadataSource = provider.SourceName
                };

                ApplyExternalMetadata(security, external, provider.SourceName);
                return security;
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static void ApplyExternalMetadata(Security security, ExternalSecuritySearchResult external, string sourceName)
    {
        if (!string.IsNullOrWhiteSpace(external.Name))
        {
            security.Name = external.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(external.AssetType))
        {
            security.AssetType = external.AssetType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(external.Currency))
        {
            security.Currency = external.Currency.Trim().ToUpperInvariant();
        }

        // Fallback: 外部 API 若未提供 currency，依據交易所推斷
        if (security.Currency == "USD" && security.Exchange is "TWSE" or "TPEX")
        {
            security.Currency = "TWD";
        }

        if (!string.IsNullOrWhiteSpace(external.Isin))
        {
            security.Isin = external.Isin.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(external.Sector))
        {
            security.Sector = external.Sector.Trim();
        }

        if (!string.IsNullOrWhiteSpace(external.Industry))
        {
            security.Industry = external.Industry.Trim();
        }

        security.MetadataUpdatedAtUtc = DateTime.UtcNow;
        security.MetadataSource = sourceName;
    }

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

    private sealed record EnsureSecurityResult(Security Security, bool Created);
}
