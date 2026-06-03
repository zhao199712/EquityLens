using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.DemoUser;

namespace EquityLens.Api.Services.Portfolios;

public sealed class PortfolioService : IPortfolioService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IDemoUserContext _demoUserContext;
    private readonly IPortfolioRepository _portfolioRepository;

    public PortfolioService(
        EquityLensDbContext dbContext,
        IDemoUserContext demoUserContext,
        IPortfolioRepository portfolioRepository)
    {
        _dbContext = dbContext;
        _demoUserContext = demoUserContext;
        _portfolioRepository = portfolioRepository;
    }

    public async Task<IReadOnlyList<PortfolioListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);
        return await _portfolioRepository.ListActiveAsync(_demoUserContext.UserId, cancellationToken);
    }

    public async Task<Result<PortfolioDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(id, _demoUserContext.UserId, cancellationToken);
        return portfolio is null
            ? Result<PortfolioDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.")
            : Result<PortfolioDetailResponse>.Success(portfolio);
    }

    public async Task<Result<PortfolioDetailResponse>> CreateAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<PortfolioDetailResponse>.Failure("portfolio.name_required", "Portfolio name is required.");
        }

        await _demoUserContext.EnsureUserAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var portfolio = new Portfolio
        {
            OwnerUserId = _demoUserContext.UserId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BaseCurrency = NormalizeCurrency(request.BaseCurrency),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _portfolioRepository.Add(portfolio);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _portfolioRepository.GetDetailAsync(portfolio.Id, _demoUserContext.UserId, cancellationToken);
        return Result<PortfolioDetailResponse>.Success(response!);
    }

    public async Task<Result<bool>> UpdateAsync(
        Guid id,
        UpdatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<bool>.Failure("portfolio.name_required", "Portfolio name is required.");
        }

        var portfolio = await _portfolioRepository.GetActiveAsync(id, _demoUserContext.UserId, cancellationToken);
        if (portfolio is null)
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        portfolio.Name = request.Name.Trim();
        portfolio.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        portfolio.BaseCurrency = NormalizeCurrency(request.BaseCurrency);
        portfolio.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetActiveAsync(id, _demoUserContext.UserId, cancellationToken);
        if (portfolio is null)
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        portfolio.IsActive = false;
        portfolio.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
