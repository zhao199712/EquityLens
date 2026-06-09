using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Securities;

namespace EquityLens.Api.Services.PortfolioHoldings;

/// <summary>
/// 投資組合持倉服務實現，提供持倉的查詢、建立、更新與刪除功能。
/// </summary>
public sealed class PortfolioHoldingService : IPortfolioHoldingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPortfolioHoldingRepository _holdingRepository;
    private readonly ISecurityService _securityService;

    /// <summary>
    /// 初始化投資組合持倉服務。
    /// </summary>
    /// <param name="dbContext">資料庫內容。</param>
    /// <param name="currentUser">目前使用者內容。</param>
    /// <param name="portfolioRepository">投資組合儲存庫。</param>
    /// <param name="holdingRepository">持倉儲存庫。</param>
    /// <param name="securityService">證券服務。</param>
    public PortfolioHoldingService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        IPortfolioHoldingRepository holdingRepository,
        ISecurityService securityService)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _holdingRepository = holdingRepository;
        _securityService = securityService;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
            request.Exchange), cancellationToken);
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    // 驗證投資組合是否存在且屬於當前使用者
    private Task<bool> PortfolioExistsAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        return _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken);
    }

    // 將貨幣代碼標準化：空白時預設為 USD，否則轉為大寫
    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
