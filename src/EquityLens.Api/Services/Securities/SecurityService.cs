using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Securities;

namespace EquityLens.Api.Services.Securities;

public sealed class SecurityService : ISecurityService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ISecurityRepository _securityRepository;

    public SecurityService(EquityLensDbContext dbContext, ISecurityRepository securityRepository)
    {
        _dbContext = dbContext;
        _securityRepository = securityRepository;
    }

    public Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        return _securityRepository.SearchAsync(query, cancellationToken);
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

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
