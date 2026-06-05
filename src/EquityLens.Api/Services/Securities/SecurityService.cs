using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.Securities;

public sealed class SecurityService : ISecurityService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;
    private readonly IEnumerable<IMarketDataProvider> _marketDataProviders;

    public SecurityService(
        EquityLensDbContext dbContext,
        ISecurityRepository securityRepository,
        IEnumerable<IMarketDataProvider> marketDataProviders)
    {
        _dbContext = dbContext;
        _securityRepository = securityRepository;
        _marketDataProviders = marketDataProviders;
    }

    public Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        return _securityRepository.SearchAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<SecuritySearchResult>> SearchAvailableAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = query.Trim();
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

        return results
            .GroupBy(x => new { x.Ticker, x.Exchange })
            .Select(x => x.OrderByDescending(result => result.SecurityId.HasValue).First())
            .OrderBy(x => x.Ticker)
            .ThenBy(x => x.Exchange)
            .Take(25)
            .ToList();
    }

    public async Task<Result<SecurityResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var security = await _securityRepository.GetAsync(id, cancellationToken);
        return security is null
            ? Result<SecurityResponse>.Failure("security.not_found", "Security was not found.")
            : Result<SecurityResponse>.Success(security);
    }

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
            IsActive = true
        };

        _securityRepository.Add(security);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _securityRepository.GetAsync(security.Id, cancellationToken);
        return Result<SecurityResponse>.Success(response!);
    }

    public async Task<Result<Security>> EnsureAsync(
        EnsureSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await EnsureCoreAsync(request, cancellationToken);
        return result.IsSuccess
            ? Result<Security>.Success(result.Value!.Security)
            : Result<Security>.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    public async Task<Result<ResolveSecurityResponse>> ResolveAsync(
        ResolveSecurityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await EnsureCoreAsync(new EnsureSecurityRequest(
            request.SecurityId,
            request.Ticker,
            request.Exchange,
            request.Name,
            request.AssetType,
            request.Currency,
            request.Isin,
            request.Sector,
            request.Industry), cancellationToken);
        if (!result.IsSuccess)
        {
            return Result<ResolveSecurityResponse>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        if (result.Value!.Created)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result<ResolveSecurityResponse>.Success(ToResolveResponse(result.Value.Security, result.Value.Created));
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
            IsActive = true
        };

        _securityRepository.Add(security);
        return Result<EnsureSecurityResult>.Success(new EnsureSecurityResult(security, true));
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

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }

    private sealed record EnsureSecurityResult(Security Security, bool Created);
}
