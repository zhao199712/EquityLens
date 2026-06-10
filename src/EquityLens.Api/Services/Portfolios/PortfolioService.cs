using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Portfolios;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.CurrentUser;

namespace EquityLens.Api.Services.Portfolios;

/// <summary>
/// 投資組合服務實現，提供投資組合的查詢、建立、更新與刪除功能。
/// </summary>
public sealed class PortfolioService : IPortfolioService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;

    /// <summary>
    /// 初始化投資組合服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="currentUser">目前使用者內容。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    public PortfolioService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortfolioListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        return await _portfolioRepository.ListActiveAsync(_currentUser.UserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PortfolioDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetDetailAsync(id, _currentUser.UserId, cancellationToken);
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

        var name = request.Name.Trim();
        if (await _portfolioRepository.ExistsByNameAsync(_currentUser.UserId, name, null, cancellationToken))
        {
            return Result<PortfolioDetailResponse>.Failure("portfolio.name_duplicate", "A portfolio with this name already exists.");
        }

        var currencyResult = NormalizeCurrency(request.BaseCurrency);
        if (!currencyResult.IsSuccess)
        {
            return Result<PortfolioDetailResponse>.Failure(currencyResult.ErrorCode!, currencyResult.ErrorMessage!);
        }

        var now = DateTime.UtcNow;
        var portfolio = new Portfolio
        {
            OwnerUserId = _currentUser.UserId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BaseCurrency = currencyResult.Value!,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _portfolioRepository.Add(portfolio);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _portfolioRepository.GetDetailAsync(portfolio.Id, _currentUser.UserId, cancellationToken);
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

        var portfolio = await _portfolioRepository.GetActiveAsync(id, _currentUser.UserId, cancellationToken);
        if (portfolio is null)
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        var name = request.Name.Trim();
        if (await _portfolioRepository.ExistsByNameAsync(_currentUser.UserId, name, id, cancellationToken))
        {
            return Result<bool>.Failure("portfolio.name_duplicate", "A portfolio with this name already exists.");
        }

        portfolio.Name = name;
        portfolio.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var currencyResult = NormalizeCurrency(request.BaseCurrency);
        if (!currencyResult.IsSuccess)
        {
            return Result<bool>.Failure(currencyResult.ErrorCode!, currencyResult.ErrorMessage!);
        }

        portfolio.BaseCurrency = currencyResult.Value!;
        portfolio.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetActiveAsync(id, _currentUser.UserId, cancellationToken);
        if (portfolio is null)
        {
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
        }

        portfolio.IsActive = false;
        portfolio.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    // 將貨幣代碼標準化：空白時預設為 TWD，否則轉為大寫；只允許 TWD 或 USD
    private static Result<string> NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return Result<string>.Success("TWD");

        var normalized = currency.Trim().ToUpperInvariant();
        return normalized is "TWD" or "USD"
            ? Result<string>.Success(normalized)
            : Result<string>.Failure("portfolio.invalid_currency", $"Unsupported currency: {currency}. Only TWD and USD are supported.");
    }
}
