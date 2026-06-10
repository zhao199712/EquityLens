using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Data;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Services.CurrentUser;

namespace EquityLens.Api.Services.PortfolioHoldings;

/// <summary>
/// 投資組合持倉服務實現，提供持倉的查詢與刪除功能。
/// </summary>
public sealed class PortfolioHoldingService : IPortfolioHoldingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPortfolioHoldingRepository _holdingRepository;
    private readonly ITransactionRepository _transactionRepository;

    public PortfolioHoldingService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        IPortfolioHoldingRepository holdingRepository,
        ITransactionRepository transactionRepository)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _holdingRepository = holdingRepository;
        _transactionRepository = transactionRepository;
    }

    public async Task<Result<IReadOnlyList<PortfolioHoldingResponse>>> ListAsync(
        Guid portfolioId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<IReadOnlyList<PortfolioHoldingResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var holdings = await _holdingRepository.ListAsync(portfolioId, cancellationToken);
        return Result<IReadOnlyList<PortfolioHoldingResponse>>.Success(holdings);
    }

    public async Task<Result<bool>> DeleteBySecurityAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");

        var holding = await _holdingRepository.GetBySecurityIdAsync(portfolioId, securityId, cancellationToken);
        if (holding is not null)
            _holdingRepository.Remove(holding);

        var transactions = await _transactionRepository.ListByHoldingAsync(portfolioId, securityId, cancellationToken);
        foreach (var tx in transactions)
            _transactionRepository.Remove(tx);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
