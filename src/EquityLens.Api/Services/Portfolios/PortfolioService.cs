using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.DemoUser;

namespace EquityLens.Api.Services.Portfolios;

/// <summary>
/// 投資組合服務實現，提供投資組合的查詢、建立、更新與刪除功能。
/// </summary>
public sealed class PortfolioService : IPortfolioService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IDemoUserContext _demoUserContext;
    private readonly IPortfolioRepository _portfolioRepository;

    /// <summary>
    /// 初始化投資組合服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="demoUserContext">演示使用者內容。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    public PortfolioService(
        EquityLensDbContext dbContext,
        IDemoUserContext demoUserContext,
        IPortfolioRepository portfolioRepository)
    {
        _dbContext = dbContext;
        _demoUserContext = demoUserContext;
        _portfolioRepository = portfolioRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortfolioListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await _demoUserContext.EnsureUserAsync(cancellationToken);
        return await _portfolioRepository.ListActiveAsync(_demoUserContext.UserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PortfolioDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(id, _demoUserContext.UserId, cancellationToken);
        return portfolio is null
            ? Result<PortfolioDetailResponse>.Failure("portfolio.not_found", "Portfolio was not found.")
            : Result<PortfolioDetailResponse>.Success(portfolio);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    // 將貨幣代碼標準化：空白時預設為 USD，否則轉為大寫
    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
