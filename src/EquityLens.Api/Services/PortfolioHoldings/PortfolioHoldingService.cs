using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.Securities;

namespace EquityLens.Api.Services.PortfolioHoldings;

public sealed class PortfolioHoldingService : IPortfolioHoldingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IDemoUserContext _demoUserContext;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPortfolioHoldingRepository _holdingRepository;
    private readonly ISecurityService _securityService;

    public PortfolioHoldingService(
        EquityLensDbContext dbContext,
        IDemoUserContext demoUserContext,
        IPortfolioRepository portfolioRepository,
        IPortfolioHoldingRepository holdingRepository,
        ISecurityService securityService)
    {
        _dbContext = dbContext;
        _demoUserContext = demoUserContext;
        _portfolioRepository = portfolioRepository;
        _holdingRepository = holdingRepository;
        _securityService = securityService;
    }

    public async Task<Result<IReadOnlyList<PortfolioHoldingResponse>>> ListAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        if (!await PortfolioExistsAsync(portfolioId, cancellationToken))
        {
            return Result<IReadOnlyList<PortfolioHoldingResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var holdings = await _holdingRepository.ListAsync(portfolioId, cancellationToken);
        return Result<IReadOnlyList<PortfolioHoldingResponse>>.Success(holdings);
    }

    public async Task<Result<PortfolioHoldingResponse>> CreateAsync(
        Guid portfolioId,
        CreatePortfolioHoldingRequest request,
        CancellationToken cancellationToken)
    {
        if (!await PortfolioExistsAsync(portfolioId, cancellationToken))
        {
            return Result<PortfolioHoldingResponse>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var securityResult = await _securityService.EnsureAsync(new EnsureSecurityRequest(
            request.SecurityId,
            request.Ticker,
            request.Exchange,
            request.Name,
            request.AssetType,
            request.Currency,
            request.Isin,
            request.Sector,
            request.Industry), cancellationToken);
        if (!securityResult.IsSuccess)
        {
            return Result<PortfolioHoldingResponse>.Failure(securityResult.ErrorCode!, securityResult.ErrorMessage!);
        }

        var security = securityResult.Value!;

        if (await _holdingRepository.SecurityHoldingExistsAsync(portfolioId, security.Id, cancellationToken))
        {
            return Result<PortfolioHoldingResponse>.Failure("holding.duplicate", "Portfolio already has a holding for this security.");
        }

        var holding = new PortfolioHolding
        {
            PortfolioId = portfolioId,
            SecurityId = security.Id,
            Quantity = request.Quantity,
            AverageCost = request.AverageCost,
            CostCurrency = NormalizeCurrency(request.CostCurrency),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            UpdatedAtUtc = DateTime.UtcNow
        };

        _holdingRepository.Add(holding);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _holdingRepository.GetResponseAsync(portfolioId, holding.Id, cancellationToken);
        return Result<PortfolioHoldingResponse>.Success(response!);
    }

    public async Task<Result<bool>> UpdateAsync(
        Guid portfolioId,
        Guid holdingId,
        UpdatePortfolioHoldingRequest request,
        CancellationToken cancellationToken)
    {
        if (!await PortfolioExistsAsync(portfolioId, cancellationToken))
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var holding = await _holdingRepository.GetAsync(portfolioId, holdingId, cancellationToken);
        if (holding is null)
        {
            return Result<bool>.Failure("holding.not_found", "Holding was not found.");
        }

        holding.Quantity = request.Quantity;
        holding.AverageCost = request.AverageCost;
        holding.CostCurrency = NormalizeCurrency(request.CostCurrency);
        holding.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        holding.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(Guid portfolioId, Guid holdingId, CancellationToken cancellationToken)
    {
        if (!await PortfolioExistsAsync(portfolioId, cancellationToken))
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var holding = await _holdingRepository.GetAsync(portfolioId, holdingId, cancellationToken);
        if (holding is null)
        {
            return Result<bool>.Failure("holding.not_found", "Holding was not found.");
        }

        _holdingRepository.Remove(holding);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    private Task<bool> PortfolioExistsAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        return _portfolioRepository.ActiveExistsAsync(portfolioId, _demoUserContext.UserId, cancellationToken);
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
